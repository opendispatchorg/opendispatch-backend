using MediatR;
using OpenDispatch.Application.Abstractions;
using OpenDispatch.Application.Jobs;
using OpenDispatch.Application.Results;
using OpenDispatch.Domain.Identifiers;
using OpenDispatch.Domain.Invoices;
using OpenDispatch.Domain.Jobs;
using OpenDispatch.Domain.ValueObjects;

namespace OpenDispatch.Application.Invoicing.GenerateInvoice;

/// <summary>
/// Raises the bill for a completed job and marks the job billed.
/// </summary>
/// <remarks>
/// <para>
/// The check step 11 left here. An <c>Invoice</c> holds a <c>JobId</c> and cannot see the job, so
/// "only completed work gets invoiced" is structurally unenforceable inside the aggregate — this
/// handler is the only place it exists, and it is also what stops a job being billed twice, since
/// a job passes through <c>Completed</c> once.
/// </para>
/// <para>
/// Two aggregates in one transaction: the invoice is raised and the job is moved to
/// <c>Invoiced</c>, or neither happens. A bill that exists against a job which does not know it has
/// been billed is the exact state the double-billing check depends on not being reachable.
/// </para>
/// <para>
/// The issue date comes from the clock rather than the command. Backdating a bill is stating a
/// fact — the aggregate takes the instant for that reason — but nothing yet asks to, and a date a
/// caller can set is a date a caller can get wrong.
/// </para>
/// <para>
/// <strong>What the invoice bills is not this handler's decision.</strong> With no lines supplied it
/// hands the whole job to <c>Invoice.FromJob</c>, which is where "what may be billed" belongs; with
/// lines supplied it adds what it was told. The only thing decided here is <em>which</em> of the two
/// happened, which is a fact about the request rather than a rule about invoicing — and the
/// emptiness check below exists so a job with nothing recorded is an expected failure a dispatcher
/// reads, rather than the <c>DomainException</c> the aggregate would otherwise throw at them.
/// </para>
/// </remarks>
internal sealed class GenerateInvoiceHandler(
    IJobRepository jobs,
    IInvoiceRepository invoices,
    IClock clock,
    ITenantContext tenant)
    : IRequestHandler<GenerateInvoiceCommand, Result<InvoiceSummary>>
{
    public async Task<Result<InvoiceSummary>> Handle(
        GenerateInvoiceCommand command,
        CancellationToken cancellationToken)
    {
        var job = await jobs.GetAsync(command.JobId, cancellationToken).ConfigureAwait(false);

        if (job is null)
        {
            return Result.Failure<InvoiceSummary>(JobErrors.NotFound(command.JobId));
        }

        if (job.Status is not JobStatus.Completed)
        {
            return Result.Failure<InvoiceSummary>(InvoiceErrors.JobNotCompleted(job.Status));
        }

        if (command.Lines is null && job.Lines.Count == 0)
        {
            return Result.Failure<InvoiceSummary>(InvoiceErrors.NothingToBill(job.Id));
        }

        var invoice = Build(job, command.Lines, tenant.OrgId, clock.UtcNow);

        invoices.Add(invoice);
        job.MarkInvoiced();

        return Result.Success(Project(invoice));
    }

    /// <summary>
    /// The bill: what the field recorded, or what the caller stated.
    /// </summary>
    /// <remarks>
    /// Two factories rather than one with a nullable argument, because they answer different
    /// questions. <c>FromJob</c> knows what a job's records mean for a bill and enforces that there
    /// is something to bill; <c>CreateFromJob</c> raises an empty draft for a caller who is about to
    /// state the lines itself, which the validator has already insisted are not none.
    /// </remarks>
    private static Invoice Build(
        Job job,
        IReadOnlyList<InvoiceLine>? stated,
        OrgId orgId,
        DateTimeOffset issued)
    {
        if (stated is null)
        {
            return Invoice.FromJob(orgId, job, issued);
        }

        var invoice = Invoice.CreateFromJob(orgId, job.Id, issued);

        foreach (var line in stated)
        {
            invoice.AddLineItem(
                line.Kind,
                line.Description,
                line.Quantity,
                Money.FromDollars(line.UnitPrice));
        }

        return invoice;
    }

    /// <summary>
    /// Projects the invoice this same call just built, so the caller sees the total the domain
    /// actually computed rather than a second, separately-rounded copy of it.
    /// </summary>
    private static InvoiceSummary Project(Invoice invoice) => new(
        invoice.Id,
        invoice.JobId,
        invoice.Status,
        invoice.Issued,
        [.. invoice.Lines.Select(line => new InvoiceLineSummary(
            line.Kind, line.Description, line.Quantity, line.UnitPrice, line.LineTotal))],
        invoice.Total);
}
