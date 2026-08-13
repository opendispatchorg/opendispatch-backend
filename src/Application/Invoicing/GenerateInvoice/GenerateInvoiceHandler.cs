using MediatR;
using OpenDispatch.Application.Abstractions;
using OpenDispatch.Application.Jobs;
using OpenDispatch.Application.Results;
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

        var invoice = Invoice.CreateFromJob(tenant.OrgId, job.Id, clock.UtcNow);

        foreach (var line in command.Lines)
        {
            invoice.AddLineItem(
                line.Kind,
                line.Description,
                line.Quantity,
                Money.FromDollars(line.UnitPrice));
        }

        invoices.Add(invoice);
        job.MarkInvoiced();

        return Result.Success(Project(invoice));
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
