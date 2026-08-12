using OpenDispatch.Domain.Common;
using OpenDispatch.Domain.Identifiers;
using OpenDispatch.Domain.Invoices;
using OpenDispatch.Domain.ValueObjects;

namespace OpenDispatch.Domain.Jobs;

/// <summary>
/// One thing the work actually took: an hour of labour, or a part fitted. Recorded by the
/// technician on site.
/// </summary>
/// <remarks>
/// <para>
/// Owned by <see cref="Job"/>, and only ever created through it. It is deliberately <em>not</em>
/// an <see cref="LineItem"/>, though the two have the same shape today: this is a record of what
/// happened, and an invoice line is a statement of what is owed. A job that is never invoiced
/// still took two hours and a capacitor.
/// </para>
/// <para>
/// They will meet when invoicing derives its lines from the job rather than from whoever raised
/// it (step 40 left that to Documents 6–7). Merging the types now would decide that in advance,
/// and would mean a technician's record of the work changing whenever somebody edited the bill.
/// </para>
/// <para>
/// <see cref="LineItemKind"/> is shared rather than copied, because "labour or a part" is one
/// vocabulary and two of them would drift.
/// </para>
/// </remarks>
public sealed class JobLine
{
    // Materialisation constructor — see the note on Job.
    private JobLine() => Description = string.Empty;

    private JobLine(
        JobLineId id,
        LineItemKind kind,
        string description,
        decimal quantity,
        Money unitPrice,
        DateTimeOffset recordedAt)
    {
        Id = id;
        Kind = kind;
        Description = description;
        Quantity = quantity;
        UnitPrice = unitPrice;
        RecordedAt = recordedAt;
    }

    /// <summary>This line's identity within its job.</summary>
    public JobLineId Id { get; private set; }

    /// <summary>Whether it is labour or a part.</summary>
    public LineItemKind Kind { get; private set; }

    /// <summary>What the technician wrote down.</summary>
    public string Description { get; private set; }

    /// <summary>How many — hours for labour, units for parts. Fractional quantities are ordinary.</summary>
    public decimal Quantity { get; private set; }

    /// <summary>What one costs.</summary>
    public Money UnitPrice { get; private set; }

    /// <summary>
    /// When the technician recorded it, by their own clock.
    /// </summary>
    /// <remarks>
    /// Kept because a line arrives hours after the work it describes and the order things were
    /// used in is what a technician would recognise. It is a device's clock, so it is evidence
    /// rather than truth.
    /// </remarks>
    public DateTimeOffset RecordedAt { get; private set; }

    /// <summary>What this line comes to, rounded to the cent.</summary>
    public Money LineTotal => UnitPrice.Multiply(Quantity);

    /// <summary>
    /// Records a line. Internal because only <see cref="Job"/> may add one.
    /// </summary>
    /// <exception cref="DomainException">The line says nothing, or records nothing.</exception>
    internal static JobLine Create(
        LineItemKind kind,
        string description,
        decimal quantity,
        Money unitPrice,
        DateTimeOffset recordedAt)
    {
        if (string.IsNullOrWhiteSpace(description))
        {
            throw new DomainException("A recorded line must say what it is for.");
        }

        if (quantity <= 0m)
        {
            throw new DomainException("A recorded line must be for a positive quantity.");
        }

        if (!Enum.IsDefined(kind))
        {
            throw new DomainException($"'{kind}' is not a kind of line a job can carry.");
        }

        return new JobLine(JobLineId.New(), kind, description.Trim(), quantity, unitPrice, recordedAt);
    }
}
