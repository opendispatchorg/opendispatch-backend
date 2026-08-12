using OpenDispatch.Application.Customers.AddServiceLocation;
using OpenDispatch.Application.Customers.CreateCustomer;
using OpenDispatch.Application.Invoicing;
using OpenDispatch.Application.Invoicing.GenerateInvoice;
using OpenDispatch.Application.Invoicing.MarkPaid;
using OpenDispatch.Application.Jobs;
using OpenDispatch.Application.Jobs.ChangeJobStatus;
using OpenDispatch.Application.Jobs.CreateJob;
using OpenDispatch.Application.Results;
using OpenDispatch.Application.Tests.Fakes;
using OpenDispatch.Domain.Events;
using OpenDispatch.Domain.Identifiers;
using OpenDispatch.Domain.Invoices;
using OpenDispatch.Domain.Jobs;
using OpenDispatch.Domain.ValueObjects;
using OpenDispatch.TestSupport;

namespace OpenDispatch.Application.Tests.Invoicing;

/// <summary>
/// Billing a finished job and settling the bill — the end of the call-to-cash loop.
/// </summary>
/// <remarks>
/// Money is one of the three things <c>TESTING.md</c> says to test exhaustively, so the totals are
/// checked at the cent and the two rules that only exist here get a case each: work is billed once
/// and only when it is finished, and a bill is settled once and only after the money moved.
/// </remarks>
[Trait(TestCategories.Name, TestCategories.Unit)]
public sealed class InvoicingTests
{
    private static readonly DateTimeOffset MondayMorning = new(2026, 8, 10, 9, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task BillsAFinishedJobForItsLabourAndPartsAndMarksItInvoiced()
    {
        await using var slice = SliceHost.Invoicing();
        var job = await ACompletedJob(slice);

        var raised = await slice.Send(new GenerateInvoiceCommand(job,
        [
            new InvoiceLine(LineItemKind.Labor, "Diagnosis and repair", 2.5m, 65m),
            new InvoiceLine(LineItemKind.Part, "Expansion vessel", 1m, 84.99m),
            new InvoiceLine(LineItemKind.Part, "Fittings", 3m, 4.5m),
        ]));

        Assert.True(raised.IsSuccess);

        var invoice = Assert.Single(slice.Store<Invoice>().Saved);
        Assert.Equal(raised.Value.Id, invoice.Id);
        Assert.Equal(slice.Tenant, invoice.OrgId);
        Assert.Equal(job, invoice.JobId);
        Assert.Equal(InvoiceStatus.Draft, invoice.Status);
        Assert.Equal(slice.Clock.UtcNow, invoice.Issued);

        // 2.5 × 65 = 162.50, plus 84.99, plus 3 × 4.50 = 13.50 — to the cent, in cents.
        Assert.Equal(new Money(16_250L + 8_499L + 1_350L), invoice.Total);
        Assert.Equal(3, invoice.Lines.Count);
        Assert.Equal(LineItemKind.Labor, invoice.Lines[0].Kind);

        // The projection the caller was actually handed says the same thing the persisted
        // aggregate does — the whole reason it is built from the invoice in hand rather than
        // re-derived from a second read.
        Assert.Equal(invoice.Total, raised.Value.Total);
        Assert.Equal(invoice.Issued, raised.Value.Issued);
        Assert.Equal(3, raised.Value.Lines.Count);

        Assert.Equal(JobStatus.Invoiced, Assert.Single(slice.Store<Job>().Saved).Status);
    }

    /// <summary>
    /// A discount is a negative price and never a negative quantity — the domain refuses the
    /// second, so with both conventions available the sign of a line would depend on which one
    /// somebody reached for.
    /// </summary>
    [Fact]
    public async Task TakesADiscountAsANegativePrice()
    {
        await using var slice = SliceHost.Invoicing();
        var job = await ACompletedJob(slice);

        await slice.Send(new GenerateInvoiceCommand(job,
        [
            new InvoiceLine(LineItemKind.Labor, "Callout", 1m, 90m),
            new InvoiceLine(LineItemKind.Labor, "Goodwill discount", 1m, -15m),
        ]));

        Assert.Equal(new Money(7_500L), Assert.Single(slice.Store<Invoice>().Saved).Total);
    }

    /// <summary>
    /// 0.333 of an hour at ninety pounds is £29.97, and the third of a cent has to land somewhere
    /// the moment the line is billed rather than drifting into the total. The example is the one
    /// <c>LineItem</c> documents, so the two cannot quietly disagree.
    /// </summary>
    [Fact]
    public async Task RoundsAFractionalLineToTheCentAsItIsBilled()
    {
        await using var slice = SliceHost.Invoicing();
        var job = await ACompletedJob(slice);

        await slice.Send(new GenerateInvoiceCommand(job,
        [
            new InvoiceLine(LineItemKind.Labor, "Part hour", 0.333m, 90m),

            // Rounded twice, and worth knowing: the price becomes whole cents before it is
            // multiplied, so 3 washers at 1.5p each is 6p and not 4.5p. Money cannot express half
            // a cent by design, so a sub-cent unit price is not a price — it is that price rounded
            // to one, and only then scaled.
            new InvoiceLine(LineItemKind.Part, "Washer", 3m, 0.015m),
        ]));

        Assert.Equal(new Money(2_997L + 6L), Assert.Single(slice.Store<Invoice>().Saved).Total);
    }

    /// <summary>
    /// The check step 11 left to whoever raises the invoice: an <c>Invoice</c> holds a
    /// <c>JobId</c> and cannot see the job, so this handler is the only thing standing between a
    /// bill and work that has not happened.
    /// </summary>
    [Theory]
    [InlineData(JobStatus.Unscheduled)]
    [InlineData(JobStatus.InProgress)]
    [InlineData(JobStatus.Cancelled)]
    public async Task RefusesToBillWorkThatIsNotFinished(JobStatus status)
    {
        await using var slice = SliceHost.Invoicing();
        var job = await AJobInStatus(slice, status);

        var raised = await slice.Send(new GenerateInvoiceCommand(job,
            [new InvoiceLine(LineItemKind.Labor, "Callout", 1m, 90m)]));

        Assert.Equal(InvoiceErrors.JobNotCompletedCode, raised.Error!.Code);
        Assert.Equal(ErrorCategory.Conflict, raised.Error.Category);
        Assert.Contains(status.ToString(), raised.Error.Message, StringComparison.Ordinal);
        Assert.Empty(slice.Store<Invoice>().Saved);
    }

    /// <summary>
    /// Billing twice is refused by the job's own state machine rather than by a "has this been
    /// invoiced" query: <c>Completed</c> is a status a job passes through once.
    /// </summary>
    [Fact]
    public async Task RefusesToBillTheSameWorkTwice()
    {
        await using var slice = SliceHost.Invoicing();
        var job = await ACompletedJob(slice);
        var lines = new[] { new InvoiceLine(LineItemKind.Labor, "Callout", 1m, 90m) };

        Assert.True((await slice.Send(new GenerateInvoiceCommand(job, lines))).IsSuccess);

        var again = await slice.Send(new GenerateInvoiceCommand(job, lines));

        Assert.Equal(InvoiceErrors.JobNotCompletedCode, again.Error!.Code);
        Assert.Single(slice.Store<Invoice>().Saved);
    }

    [Fact]
    public async Task TakesTheMoneyThenSettlesTheBillAndTheJob()
    {
        await using var slice = SliceHost.Invoicing();
        var invoice = await ABilledJob(slice);

        // The job has been driven through its whole life to get here, so what matters is that
        // settling it adds nothing to what it already said.
        var announcedByTheWork = Assert.Single(slice.Store<Job>().Saved).DomainEvents.Count;

        var paid = await slice.Send(new MarkPaidCommand(invoice));

        Assert.True(paid.IsSuccess);

        var settled = Assert.Single(slice.Store<Invoice>().Saved);
        Assert.Equal(InvoiceStatus.Paid, settled.Status);
        Assert.Equal(JobStatus.Paid, Assert.Single(slice.Store<Job>().Saved).Status);

        // The gateway was asked for the invoice's own total, against the invoice's own identity —
        // which is what makes a retry recognisable to a processor that has one.
        var charge = Assert.Single(slice.Fake<ControllableGateway>().Charges);
        Assert.Equal(settled.Total, charge.Amount);
        Assert.Equal(settled.Id, charge.Invoice);

        // The invoice announces the payment; the job's own status change says nothing, because a
        // second event about the same fact is two announcements of one thing.
        var announced = Assert.IsType<InvoicePaid>(Assert.Single(settled.DomainEvents));
        Assert.Equal(settled.Id, announced.InvoiceId);
        Assert.Equal(settled.JobId, announced.JobId);
        Assert.Equal(announcedByTheWork, Assert.Single(slice.Store<Job>().Saved).DomainEvents.Count);
    }

    /// <summary>
    /// The ordering this handler exists to get right: money first, marking second. v1's gateway
    /// never refuses, so only a fake can reach this — which is exactly why the fake can.
    /// </summary>
    [Fact]
    public async Task LeavesEverythingUnpaidWhenTheMoneyIsRefused()
    {
        await using var slice = SliceHost.Invoicing();
        var invoice = await ABilledJob(slice);
        slice.Fake<ControllableGateway>().Refusing = "The card was declined.";

        var paid = await slice.Send(new MarkPaidCommand(invoice));

        Assert.Equal(InvoiceErrors.PaymentRefusedCode, paid.Error!.Code);
        Assert.Equal("The card was declined.", paid.Error.Message);

        Assert.Equal(InvoiceStatus.Draft, Assert.Single(slice.Store<Invoice>().Saved).Status);
        Assert.Equal(JobStatus.Invoiced, Assert.Single(slice.Store<Job>().Saved).Status);
        Assert.Empty(Assert.Single(slice.Store<Invoice>().Saved).DomainEvents);
    }

    /// <summary>
    /// Paying twice means two payments were taken or one was recorded against the wrong bill.
    /// Answering "done" would hide both — and the second charge must not even be attempted.
    /// </summary>
    [Fact]
    public async Task RefusesToSettleABillThatIsAlreadyPaidAndDoesNotChargeAgain()
    {
        await using var slice = SliceHost.Invoicing();
        var invoice = await ABilledJob(slice);
        await slice.Send(new MarkPaidCommand(invoice));

        var again = await slice.Send(new MarkPaidCommand(invoice));

        Assert.Equal(InvoiceErrors.AlreadyPaidCode, again.Error!.Code);
        Assert.Equal(ErrorCategory.Conflict, again.Error.Category);
        Assert.Single(slice.Fake<ControllableGateway>().Charges);
    }

    [Fact]
    public async Task RefusesAnInvoiceThisTenantDoesNotHave()
    {
        await using var slice = SliceHost.Invoicing();

        var paid = await slice.Send(new MarkPaidCommand(InvoiceId.New()));

        Assert.Equal(InvoiceErrors.NotFoundCode, paid.Error!.Code);
        Assert.Empty(slice.Fake<ControllableGateway>().Charges);
    }

    [Fact]
    public async Task RefusesAnInvoiceWithNothingOnIt()
    {
        await using var slice = SliceHost.Invoicing();
        var job = await ACompletedJob(slice);

        var raised = await slice.Send(new GenerateInvoiceCommand(job, []));

        var failure = Assert.IsType<ValidationError>(raised.Error);
        Assert.Equal(
            "An invoice must bill for something.",
            Assert.Single(failure.Failures[nameof(GenerateInvoiceCommand.Lines)]));
        Assert.Equal(JobStatus.Completed, Assert.Single(slice.Store<Job>().Saved).Status);
    }

    /// <summary>
    /// <c>Money</c> is checked arithmetic, so a slipped decimal point is an
    /// <c>OverflowException</c> from inside the domain rather than a wrong total. These bounds
    /// catch the typing mistake before it gets there.
    /// </summary>
    /// <remarks>
    /// The cases are doubles because an attribute cannot carry a decimal; they are exact at these
    /// magnitudes and converted before they reach the command.
    /// </remarks>
    [Theory]
    [InlineData(0d, 90d)]
    [InlineData(-1d, 90d)]
    [InlineData(1d, 9_000_000d)]
    [InlineData(1_000_000d, 90d)]
    public async Task RefusesALineThatCouldNotBeBilled(double quantity, double unitPrice)
    {
        await using var slice = SliceHost.Invoicing();
        var job = await ACompletedJob(slice);

        var raised = await slice.Send(new GenerateInvoiceCommand(job,
            [new InvoiceLine(LineItemKind.Labor, "Callout", (decimal)quantity, (decimal)unitPrice)]));

        Assert.IsType<ValidationError>(raised.Error);
        Assert.Empty(slice.Store<Invoice>().Saved);
    }

    [Fact]
    public async Task RefusesABillForAJobThisTenantDoesNotHave()
    {
        await using var slice = SliceHost.Invoicing();

        var raised = await slice.Send(new GenerateInvoiceCommand(JobId.New(),
            [new InvoiceLine(LineItemKind.Labor, "Callout", 1m, 90m)]));

        Assert.Equal(JobErrors.NotFoundCode, raised.Error!.Code);
    }

    private static async Task<InvoiceId> ABilledJob(SliceHost slice)
    {
        var job = await ACompletedJob(slice);
        var raised = await slice.Send(new GenerateInvoiceCommand(job,
            [new InvoiceLine(LineItemKind.Labor, "Diagnosis and repair", 2.5m, 65m)]));

        return raised.Value.Id;
    }

    private static Task<JobId> ACompletedJob(SliceHost slice) =>
        AJobInStatus(slice, JobStatus.Completed);

    private static async Task<JobId> AJobInStatus(SliceHost slice, JobStatus status)
    {
        var customer = await slice.Send(new CreateCustomerCommand("Vance Refrigeration", null, null));
        var location = await slice.Send(new AddServiceLocationCommand(
            customer.Value,
            "Site",
            "12 Bath Road",
            51.5107,
            -0.5950));

        var job = await slice.Send(new CreateJobCommand(
            customer.Value,
            location.Value,
            "hvac",
            JobPriority.Normal,
            MondayMorning,
            MondayMorning.AddHours(3),
            TimeSpan.FromHours(1)));

        JobStatus[] walk = status is JobStatus.Cancelled
            ? [JobStatus.Cancelled]
            : [JobStatus.Scheduled, JobStatus.Dispatched, JobStatus.EnRoute, JobStatus.InProgress, JobStatus.Completed];

        foreach (var step in walk)
        {
            if (step > status && status is not JobStatus.Cancelled)
            {
                break;
            }

            await slice.Send(new ChangeJobStatusCommand(job.Value, step));

            if (step == status)
            {
                break;
            }
        }

        return job.Value;
    }
}
