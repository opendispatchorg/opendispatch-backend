using System.Text;
using DomainJobStatus = OpenDispatch.Domain.Jobs.JobStatus;

namespace OpenDispatch.Contracts.CodeGen;

/// <summary>
/// Emits the job state machine's transition table into the generated package, with a
/// <c>canTransition</c> helper over it.
/// </summary>
/// <remarks>
/// <para>
/// This is the point at which the single source of truth stops being about shapes and starts
/// being about rules. The technician app decides offline which buttons to offer (Document 6
/// §5); without this it would decide from a hand-copied lifecycle, and the first time the two
/// disagreed a technician would tap a button the server then refused.
/// </para>
/// <para>
/// The table is Domain data and stays there — <c>Contracts</c> has no Domain reference, by
/// Document 2 §2 — so this tool, which may see both, is where the two meet. Nothing is
/// interpreted on the way through: the emitted rows are the rows the aggregate enforces,
/// which is what the parity test asserts.
/// </para>
/// </remarks>
public static class JobTransitionsEmitter
{
    /// <summary>The name of the emitted table, and the prefix of everything else here.</summary>
    public const string TableName = "jobTransitions";

    /// <summary>The name of the emitted predicate.</summary>
    public const string HelperName = "canTransition";

    /// <summary>
    /// Renders <paramref name="transitions"/> as a TypeScript section, appended to the wire
    /// shapes so it sits beside the <c>JobStatus</c> it is expressed in.
    /// </summary>
    /// <param name="transitions">
    /// The domain's table, whole. Rows and targets are written in lifecycle order rather than
    /// whatever order the dictionary enumerates in, because the file is diffed for drift.
    /// </param>
    public static string Emit(IReadOnlyDictionary<DomainJobStatus, IReadOnlySet<DomainJobStatus>> transitions)
    {
        ArgumentNullException.ThrowIfNull(transitions);

        var text = new StringBuilder("\n")
            .Append(TypeScriptText.Banner("Job state machine — generated from the domain transition table"))
            .Append(TypeScriptText.Documentation(
                "What may follow what. The same table the server enforces, so a client deciding "
                    + "which action to offer is reading the rule rather than guessing at it. A "
                    + "status with no successors is terminal."))
            .Append("export const ").Append(TableName)
            .Append(": Readonly<Record<JobStatus, readonly JobStatus[]>> = {\n");

        // Annotating the constant rather than inferring it is what makes a missing row a
        // compile error in the generated package: Record<JobStatus, ...> wants every status.
        foreach (var (from, to) in InLifecycleOrder(transitions))
        {
            text.Append("  ").Append(from).Append(": [")
                .Append(string.Join(", ", to.Select(status => $"'{status}'")))
                .Append("],\n");
        }

        return text.Append("};\n\n")
            .Append(TypeScriptText.Documentation(
                "Whether a job may move from one status to the next. The client-side half of the "
                    + "state machine: the server still refuses an illegal move, and a device that "
                    + "has been offline long enough may be asking about a job that has already "
                    + "moved on."))
            .Append("export function ").Append(HelperName)
            .Append("(from: JobStatus, to: JobStatus): boolean {\n")
            .Append("  return ").Append(TableName).Append("[from].includes(to);\n")
            .Append("}\n")
            .ToString();
    }

    private static IEnumerable<(DomainJobStatus From, IEnumerable<DomainJobStatus> To)> InLifecycleOrder(
        IReadOnlyDictionary<DomainJobStatus, IReadOnlySet<DomainJobStatus>> transitions) =>
        transitions
            .OrderBy(row => row.Key)
            .Select(row => (row.Key, row.Value.OrderBy(status => status).AsEnumerable()));
}
