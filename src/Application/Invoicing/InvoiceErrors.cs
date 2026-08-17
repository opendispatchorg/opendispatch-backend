using OpenDispatch.Application.Results;
using OpenDispatch.Domain.Identifiers;
using OpenDispatch.Domain.Jobs;

namespace OpenDispatch.Application.Invoicing;

/// <summary>
/// The expected failures the invoicing slices can report.
/// </summary>
public static class InvoiceErrors
{
    /// <summary>The code every "no such invoice" failure carries.</summary>
    public const string NotFoundCode = "invoice.notFound";

    /// <summary>The code every attempt to bill unfinished or already-billed work carries.</summary>
    public const string JobNotCompletedCode = "invoice.jobNotCompleted";

    /// <summary>The code a job with nothing billable on it carries.</summary>
    public const string NothingToBillCode = "invoice.nothingToBill";

    /// <summary>The code every attempt to settle a settled invoice carries.</summary>
    public const string AlreadyPaidCode = "invoice.alreadyPaid";

    /// <summary>The code a refused payment carries.</summary>
    public const string PaymentRefusedCode = "payment.refused";

    /// <summary>Names an invoice this tenant does not have.</summary>
    /// <param name="id">The invoice that was asked for.</param>
    public static Error NotFound(InvoiceId id) =>
        Error.NotFound(NotFoundCode, $"There is no invoice {id.Value}.");

    /// <summary>
    /// Reports a job that cannot be billed.
    /// </summary>
    /// <remarks>
    /// <para>
    /// One error for two situations, because the status tells them apart and the caller needs to
    /// know which: work that is not finished yet, and work that has already been billed. Step 11
    /// left this check to whoever raises the invoice — an <c>Invoice</c> holds a <c>JobId</c> and
    /// cannot see the job — and this is that check, in the only place it exists.
    /// </para>
    /// <para>
    /// It is also what stops a job being billed twice, without the repository needing an
    /// "invoice for this job" query: <c>Completed</c> is a status a job passes through once.
    /// </para>
    /// </remarks>
    /// <param name="status">Where the job actually is.</param>
    public static Error JobNotCompleted(JobStatus status) =>
        Error.Conflict(
            JobNotCompletedCode,
            $"Only completed work can be invoiced, and this job is {status}.");

    /// <summary>
    /// Reports a completed job that records nothing an invoice could be built from.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Reachable only when the caller left the lines out — "bill what the visit took" — and the
    /// visit recorded nothing: no labour, no parts, because nobody entered any on the phone. A
    /// refusal rather than a zero invoice, because an empty bill sent to a customer is worse than
    /// an answer the office can see, and because raising one would spend the job's single
    /// <c>Completed → Invoiced</c> transition on a document with nothing on it.
    /// </para>
    /// <para>
    /// The fix is either half: record the work on the phone, or state the lines in the request.
    /// The message says so, because the person reading it is a dispatcher rather than a developer.
    /// </para>
    /// </remarks>
    /// <param name="id">The job that has nothing to bill.</param>
    public static Error NothingToBill(JobId id) =>
        Error.Conflict(
            NothingToBillCode,
            $"Job {id.Value} has no recorded labour or parts to bill. Record them on the job, or "
            + "state the lines on the invoice.");

    /// <summary>Reports an invoice that has already been settled.</summary>
    /// <remarks>
    /// Not an idempotent no-op. Paying twice means two payments were taken or one was recorded
    /// against the wrong bill, and either way somebody needs to know rather than be told "done".
    /// </remarks>
    /// <param name="id">The invoice that was asked for.</param>
    public static Error AlreadyPaid(InvoiceId id) =>
        Error.Conflict(AlreadyPaidCode, $"Invoice {id.Value} has already been paid.");

    /// <summary>
    /// Reports money the gateway would not take.
    /// </summary>
    /// <remarks>
    /// A refused card is an ordinary answer rather than an exception — it is somebody's bank
    /// declining, and the dispatcher has to be told in words. v1's gateway never refuses, which
    /// makes this the one error here with no test that can reach it through the real adapter.
    /// </remarks>
    /// <param name="reason">What the gateway said, in terms fit to show a person.</param>
    public static Error PaymentRefused(string reason) =>
        Error.Conflict(PaymentRefusedCode, reason);
}
