using OpenDispatch.Domain.Common;
using OpenDispatch.Domain.Events;
using OpenDispatch.Domain.Identifiers;
using OpenDispatch.Domain.Jobs;
using OpenDispatch.Domain.ValueObjects;

namespace OpenDispatch.Domain.Invoices;

/// <summary>
/// The bill for a job: what was done, what it cost, and whether it has been paid.
/// </summary>
/// <remarks>
/// <para>
/// It points at its job by <see cref="JobId"/> and never holds the job itself, so it cannot
/// check that the work is actually finished — that belongs to whoever decides to raise the
/// invoice, one aggregate up.
/// </para>
/// <para>
/// A paid invoice is closed to changes. Adding a line to a settled bill would move a total
/// somebody has already paid against, so it is refused rather than merely discouraged.
/// </para>
/// </remarks>
public sealed class Invoice : AggregateRoot
{
    private readonly List<LineItem> _lines = [];

    // Materialisation constructor — see the note on Job.
    private Invoice()
    {
    }

    private Invoice(InvoiceId id, OrgId orgId, JobId jobId, DateTimeOffset issued)
    {
        Id = id;
        OrgId = orgId;
        JobId = jobId;
        Issued = issued;
        Status = InvoiceStatus.Draft;
    }

    /// <summary>This invoice's identity.</summary>
    public InvoiceId Id { get; private set; }

    /// <summary>The tenant billing for the work.</summary>
    public OrgId OrgId { get; private set; }

    /// <summary>The job this bills for. A reference, never a navigation property.</summary>
    public JobId JobId { get; private set; }

    /// <summary>Whether it has been settled.</summary>
    public InvoiceStatus Status { get; private set; }

    /// <summary>When it was raised.</summary>
    public DateTimeOffset Issued { get; private set; }

    /// <summary>The billed lines, in the order they were added.</summary>
    public IReadOnlyList<LineItem> Lines => _lines.AsReadOnly();

    /// <summary>
    /// What is owed. Computed from the lines every time rather than kept alongside them,
    /// so it cannot disagree with them.
    /// </summary>
    /// <exception cref="OverflowException">The lines sum past what a money amount can hold.</exception>
    public Money Total => _lines.Aggregate(Money.Zero, (running, line) => running.Add(line.LineTotal));

    /// <summary>
    /// Raises a draft invoice for a job, with no lines on it yet.
    /// </summary>
    /// <remarks>
    /// The issue date is passed in rather than read from a clock, so backdating a bill for
    /// work finished last week is stating a fact rather than fighting the domain.
    /// </remarks>
    public static Invoice CreateFromJob(OrgId orgId, JobId jobId, DateTimeOffset issued) =>
        Announce(new Invoice(InvoiceId.New(), orgId, jobId, issued));

    /// <summary>
    /// Raises a draft invoice billing exactly what the technician recorded against the job.
    /// </summary>
    /// <param name="orgId">The tenant billing for the work.</param>
    /// <param name="job">The finished work, with the lines the field wrote down.</param>
    /// <param name="issued">When the bill is raised.</param>
    /// <exception cref="DomainException">
    /// The job records nothing to bill. A zero invoice is not the answer: an empty bill sent to a
    /// customer is worse than a refusal an office can see and act on, and the two ways to get here
    /// — nobody recorded anything, or somebody meant to type the lines — both want a human.
    /// </exception>
    /// <remarks>
    /// <para>
    /// <strong>What may be billed is an invoicing rule, so it lives here.</strong> A handler that
    /// walked the job's lines and called <see cref="AddLineItem"/> would be a second opinion about
    /// which of a job's records are billable, sitting outside the aggregate that owns the question —
    /// and the day "labour under fifteen minutes is not charged" or "warranty parts are recorded but
    /// not billed" arrives, it would be the place that quietly disagreed.
    /// </para>
    /// <para>
    /// <strong>A copy, not a reference.</strong> A <c>JobLine</c> is a record of what happened and a
    /// <see cref="LineItem"/> is a statement of what is owed — <c>JobLine</c>'s own remarks draw the
    /// distinction, and this is the meeting it anticipated. Copying is what keeps them separate
    /// afterwards: correcting a bill must not rewrite the technician's account of the visit, and a
    /// line recorded after the invoice was raised must not silently change what was billed.
    /// </para>
    /// </remarks>
    public static Invoice FromJob(OrgId orgId, Job job, DateTimeOffset issued)
    {
        ArgumentNullException.ThrowIfNull(job);

        if (job.Lines.Count == 0)
        {
            throw new DomainException($"Job {job.Id.Value} records nothing that can be billed.");
        }

        var invoice = new Invoice(InvoiceId.New(), orgId, job.Id, issued);

        foreach (var recorded in job.Lines)
        {
            invoice.AddLineItem(recorded.Kind, recorded.Description, recorded.Quantity, recorded.UnitPrice);
        }

        return Announce(invoice);
    }

    /// <summary>
    /// Says that a bill now exists, whichever factory built it.
    /// </summary>
    /// <remarks>
    /// Both factories go through here rather than each raising for itself: two build sites and one
    /// announcement is the arrangement that cannot be half-done, and a third would inherit it. The
    /// event carries no total on purpose — see <see cref="InvoiceRaised"/> — which is what lets it
    /// be raised before <c>CreateFromJob</c>'s caller has added a single line.
    /// </remarks>
    private static Invoice Announce(Invoice invoice)
    {
        invoice.Raise(new InvoiceRaised(invoice.Id, invoice.JobId));

        return invoice;
    }

    /// <summary>Bills for something — time on the job, or a part fitted.</summary>
    /// <exception cref="DomainException">The invoice is settled, or the line bills nothing.</exception>
    public void AddLineItem(LineItemKind kind, string description, decimal quantity, Money unitPrice)
    {
        if (Status is not InvoiceStatus.Draft)
        {
            throw new DomainException("A settled invoice cannot be added to.");
        }

        _lines.Add(LineItem.Create(kind, description, quantity, unitPrice));
    }

    /// <summary>Settles the invoice.</summary>
    /// <exception cref="DomainException">
    /// It has already been settled. Paying twice is not an idempotent no-op — it means two
    /// payments were taken, or one was recorded against the wrong bill, and either way a
    /// human needs to know.
    /// </exception>
    public void MarkPaid()
    {
        if (Status is not InvoiceStatus.Draft)
        {
            throw new DomainException($"Invoice {Id.Value} is already paid.");
        }

        Status = InvoiceStatus.Paid;

        Raise(new InvoicePaid(Id, JobId));
    }
}
