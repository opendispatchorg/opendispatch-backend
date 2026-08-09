using OpenDispatch.Domain.Common;
using OpenDispatch.Domain.Events;
using OpenDispatch.Domain.Identifiers;
using OpenDispatch.Domain.Invoices;
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
}
