namespace OpenDispatch.Domain.Invoices;

/// <summary>
/// What a billed line is for.
/// </summary>
/// <remarks>
/// The split exists because the two are reported on differently — labour against technician
/// utilisation, parts against stock — and because inventory later subscribes to parts being
/// used. Values are numbered explicitly because this enum is mirrored to the clients.
/// </remarks>
public enum LineItemKind
{
    /// <summary>Time on the job, billed by the hour.</summary>
    Labor = 0,

    /// <summary>Something fitted or supplied, billed by the unit.</summary>
    Part = 1,
}
