using MediatR;
using OpenDispatch.Application.Abstractions;
using OpenDispatch.Application.Jobs;
using OpenDispatch.Application.Results;
using OpenDispatch.Domain.Invoices;
using OpenDispatch.Domain.Jobs;

namespace OpenDispatch.Application.Invoicing.MarkPaid;

/// <summary>
/// Charges the invoice's total, then settles the bill and the job.
/// </summary>
/// <remarks>
/// <para>
/// The order matters and is the wrong way round from what a reader might expect: the money is
/// taken first, and only then is anything marked paid. Marking first and charging second would
/// leave a settled invoice against money nobody took if the gateway refused — and refusal is an
/// ordinary answer, not an exception.
/// </para>
/// <para>
/// <strong>What that ordering costs, stated plainly:</strong> the charge happens inside the
/// transaction, so a failure <em>after</em> it — the save, the commit, the process — means money
/// taken and nothing recorded. v1's gateway takes no money, so this is a real exposure only when a
/// processor is real, and the port is already shaped for the answer: <c>ChargeAsync</c> takes the
/// invoice's identity precisely so a retry is recognised rather than charged twice.
/// </para>
/// <para>
/// The reference the gateway hands back is not stored. <c>Invoice</c> has nowhere to put it and
/// <c>MarkPaid()</c> takes no argument; v1's reference identifies a payment that did not happen, so
/// a column for it would hold nothing worth reconciling. The day a real processor lands, storing it
/// is a field, a migration, and the same conversation as idempotency.
/// </para>
/// </remarks>
internal sealed class MarkPaidHandler(
    IInvoiceRepository invoices,
    IJobRepository jobs,
    IPaymentGateway payments)
    : IRequestHandler<MarkPaidCommand, Result>
{
    public async Task<Result> Handle(MarkPaidCommand command, CancellationToken cancellationToken)
    {
        var invoice = await invoices.GetAsync(command.InvoiceId, cancellationToken).ConfigureAwait(false);

        if (invoice is null)
        {
            return Result.Failure(InvoiceErrors.NotFound(command.InvoiceId));
        }

        // Asked before the money moves, not caught afterwards: MarkPaid refuses a settled invoice
        // by throwing, and taking payment for a bill that is about to refuse it would be the worst
        // possible order to find that out in.
        if (invoice.Status is not InvoiceStatus.Draft)
        {
            return Result.Failure(InvoiceErrors.AlreadyPaid(invoice.Id));
        }

        var job = await jobs.GetAsync(invoice.JobId, cancellationToken).ConfigureAwait(false);

        if (job is null)
        {
            return Result.Failure(JobErrors.NotFound(invoice.JobId));
        }

        if (!job.CanTransition(JobStatus.Paid))
        {
            return Result.Failure(JobErrors.IllegalTransition(job.Status, JobStatus.Paid));
        }

        var payment = await payments
            .ChargeAsync(invoice.Total, invoice.Id, cancellationToken)
            .ConfigureAwait(false);

        if (!payment.Succeeded)
        {
            return Result.Failure(InvoiceErrors.PaymentRefused(payment.Failure!));
        }

        invoice.MarkPaid();
        job.MarkPaid();

        return Result.Success();
    }
}
