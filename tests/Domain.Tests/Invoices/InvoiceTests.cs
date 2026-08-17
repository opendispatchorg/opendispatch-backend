using OpenDispatch.Domain.Common;
using OpenDispatch.Domain.Events;
using OpenDispatch.Domain.Identifiers;
using OpenDispatch.Domain.Invoices;
using OpenDispatch.Domain.Jobs;
using OpenDispatch.Domain.ValueObjects;
using OpenDispatch.TestSupport;
using OpenDispatch.TestSupport.Builders;

namespace OpenDispatch.Domain.Tests.Invoices;

/// <summary>
/// Two things here are worth being thorough about: a total that is a cent out is a bug
/// somebody notices in their bank account, and paying twice must not be quietly absorbed.
/// </summary>
[Trait(TestCategories.Name, TestCategories.Unit)]
public sealed class InvoiceTests
{
    /// <summary>When the technician wrote a line down, by their own clock.</summary>
    private static readonly DateTimeOffset OnSite = new(2026, 8, 10, 11, 30, 0, TimeSpan.Zero);

    /// <summary>When the office raised the bill.</summary>
    private static readonly DateTimeOffset Issued = new(2026, 8, 10, 17, 0, 0, TimeSpan.Zero);

    [Fact]
    public void ANewInvoiceIsADraftOwingNothing()
    {
        var invoice = InvoiceBuilder.Any().Build();

        Assert.Equal(InvoiceStatus.Draft, invoice.Status);
        Assert.Empty(invoice.Lines);
        Assert.Equal(Money.Zero, invoice.Total);
        Assert.Empty(invoice.DomainEvents);
    }

    [Fact]
    public void TotalSumsLabourAndParts()
    {
        var invoice = InvoiceBuilder.Any().Build();

        invoice.AddLineItem(LineItemKind.Labor, "Diagnostic and repair", 2.5m, Money.FromDollars(85m));
        invoice.AddLineItem(LineItemKind.Part, "Capacitor", 1m, Money.FromDollars(42.50m));
        invoice.AddLineItem(LineItemKind.Part, "Air filter", 3m, Money.FromDollars(19.99m));

        // 212.50 + 42.50 + 59.97
        Assert.Equal(Money.FromDollars(314.97m), invoice.Total);
    }

    [Fact]
    public void TotalCarriesADiscountLineAsANegativePrice()
    {
        var invoice = InvoiceBuilder.Any().Build();

        invoice.AddLineItem(LineItemKind.Labor, "Callout", 1m, Money.FromDollars(120m));
        invoice.AddLineItem(LineItemKind.Labor, "Goodwill discount", 1m, Money.FromDollars(-20m));

        Assert.Equal(Money.FromDollars(100m), invoice.Total);
    }

    [Fact]
    public void ALineTotalRoundsToTheCentRatherThanCarryingAFraction()
    {
        var invoice = InvoiceBuilder.Any().Build();

        // A third of an hour at $90 is $29.97, not $29.9700000...
        invoice.AddLineItem(LineItemKind.Labor, "Part hour", 0.333m, Money.FromDollars(90m));

        Assert.Equal(Money.FromDollars(29.97m), invoice.Total);
    }

    [Fact]
    public void MarkPaidSettlesTheInvoiceAndAnnouncesIt()
    {
        var job = JobId.New();
        var invoice = InvoiceBuilder.Any().ForJob(job).Build();
        invoice.AddLineItem(LineItemKind.Labor, "Callout", 1m, Money.FromDollars(120m));

        invoice.MarkPaid();

        Assert.Equal(InvoiceStatus.Paid, invoice.Status);
        var paid = Assert.IsType<InvoicePaid>(Assert.Single(invoice.DomainEvents));
        Assert.Equal(invoice.Id, paid.InvoiceId);
        Assert.Equal(job, paid.JobId);
    }

    [Fact]
    public void PayingATwiceSettledInvoiceIsRefusedAndAnnouncesNothingFurther()
    {
        var invoice = InvoiceBuilder.Any().Build();
        invoice.MarkPaid();
        invoice.ClearDomainEvents();

        Assert.Throws<DomainException>(invoice.MarkPaid);

        Assert.Equal(InvoiceStatus.Paid, invoice.Status);
        Assert.Empty(invoice.DomainEvents);
    }

    [Fact]
    public void ASettledInvoiceCannotBeAddedTo()
    {
        var invoice = InvoiceBuilder.Any().Build();
        invoice.AddLineItem(LineItemKind.Labor, "Callout", 1m, Money.FromDollars(120m));
        invoice.MarkPaid();

        Assert.Throws<DomainException>(
            () => invoice.AddLineItem(LineItemKind.Part, "Capacitor", 1m, Money.FromDollars(42.50m)));

        Assert.Equal(Money.FromDollars(120m), invoice.Total);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void RejectsALineThatSaysNothing(string description)
    {
        var invoice = InvoiceBuilder.Any().Build();

        Assert.Throws<DomainException>(
            () => invoice.AddLineItem(LineItemKind.Part, description, 1m, Money.FromDollars(10m)));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void RejectsALineForNothingOrLess(int quantity)
    {
        // A credit is a negative price, not a negative quantity — otherwise the sign of a
        // line total depends on which of two fields someone chose to make negative.
        var invoice = InvoiceBuilder.Any().Build();

        Assert.Throws<DomainException>(
            () => invoice.AddLineItem(LineItemKind.Part, "Capacitor", quantity, Money.FromDollars(10m)));
    }

    [Fact]
    public void ARefusedLineLeavesTheInvoiceUntouched()
    {
        var invoice = InvoiceBuilder.Any().Build();
        invoice.AddLineItem(LineItemKind.Labor, "Callout", 1m, Money.FromDollars(120m));

        Assert.Throws<DomainException>(
            () => invoice.AddLineItem(LineItemKind.Part, "  ", 1m, Money.FromDollars(10m)));

        Assert.Single(invoice.Lines);
        Assert.Equal(Money.FromDollars(120m), invoice.Total);
    }

    /// <summary>
    /// Document 1's "turn a completed job into an invoice from its labor and parts", as one
    /// assertion: what the technician wrote down on site is what the customer is billed for, with
    /// nobody in the office re-typing it.
    /// </summary>
    [Fact]
    public void BillsExactlyWhatTheTechnicianRecorded()
    {
        var job = JobBuilder.Any().InStatus(JobStatus.Completed).Build();

        job.RecordLine(LineItemKind.Labor, "Two hours on the roof", 2m, Money.FromDollars(85m), OnSite);
        job.RecordLine(LineItemKind.Part, "Capacitor", 1m, Money.FromDollars(42.50m), OnSite);

        var invoice = Invoice.FromJob(OrgId.New(), job, Issued);

        // 170.00 + 42.50
        Assert.Equal(Money.FromDollars(212.50m), invoice.Total);
        Assert.Equal(InvoiceStatus.Draft, invoice.Status);
        Assert.Equal(job.Id, invoice.JobId);

        Assert.Collection(
            invoice.Lines,
            labour =>
            {
                Assert.Equal(LineItemKind.Labor, labour.Kind);
                Assert.Equal("Two hours on the roof", labour.Description);
                Assert.Equal(2m, labour.Quantity);
                Assert.Equal(Money.FromDollars(85m), labour.UnitPrice);
            },
            part => Assert.Equal("Capacitor", part.Description));
    }

    /// <summary>
    /// The two records stay separate afterwards. Correcting a bill must not rewrite the
    /// technician's account of the visit, and work recorded after the invoice was raised must not
    /// silently change what was billed — which is what a shared collection would do.
    /// </summary>
    [Fact]
    public void TheBillIsACopyOfTheRecordAndNotTheRecordItself()
    {
        var job = JobBuilder.Any().InStatus(JobStatus.Completed).Build();
        job.RecordLine(LineItemKind.Labor, "Callout", 1m, Money.FromDollars(120m), OnSite);

        var invoice = Invoice.FromJob(OrgId.New(), job, Issued);

        job.RecordLine(LineItemKind.Part, "Thermostat, fitted next day", 1m, Money.FromDollars(180m), OnSite);

        Assert.Single(invoice.Lines);
        Assert.Equal(Money.FromDollars(120m), invoice.Total);
        Assert.Equal(2, job.Lines.Count);
    }

    /// <summary>
    /// A visit that recorded nothing is refused rather than billed for nothing. A zero invoice is
    /// arithmetic the type permits and a document no shop sends, and raising one would spend the
    /// job's single <c>Completed → Invoiced</c> transition on an empty bill.
    /// </summary>
    [Fact]
    public void RefusesToBillAJobThatRecordedNothing()
    {
        var job = JobBuilder.Any().InStatus(JobStatus.Completed).Build();

        Assert.Throws<DomainException>(() => Invoice.FromJob(OrgId.New(), job, Issued));
    }
}
