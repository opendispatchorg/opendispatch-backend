using OpenDispatch.Application.Results;
using OpenDispatch.Domain.Identifiers;

namespace OpenDispatch.Application.Technicians;

/// <summary>
/// The expected failures this slice can report.
/// </summary>
/// <remarks>
/// As with customers, "this tenant has no such technician" covers both one that never existed and
/// one on another organization's books: the query filters mean this tenant genuinely cannot see
/// them, and an answer that told the two apart would confirm somebody else's row exists.
/// </remarks>
public static class TechnicianErrors
{
    /// <summary>The code every "no such technician" failure carries.</summary>
    public const string NotFoundCode = "technician.notFound";

    /// <summary>Names a technician this tenant does not have.</summary>
    /// <param name="id">The technician that was asked for.</param>
    public static Error NotFound(TechnicianId id) =>
        Error.NotFound(NotFoundCode, $"There is no technician {id.Value}.");
}
