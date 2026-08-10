using OpenDispatch.Domain.Common;
using OpenDispatch.Domain.Events;
using OpenDispatch.Domain.Identifiers;
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
        new(InvoiceId.New(), orgId, jobId, issued);

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
