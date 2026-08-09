using System.Text.RegularExpressions;
using OpenDispatch.Contracts.CodeGen;
using OpenDispatch.Domain.Jobs;
using OpenDispatch.TestSupport;
using DomainJobStatus = OpenDispatch.Domain.Jobs.JobStatus;

namespace OpenDispatch.Contracts.Tests;

/// <summary>
/// The generated transition table against the one the aggregate enforces. This is the only
/// test in the suite that reads generated TypeScript back, and it earns that: the whole point
/// of exporting the rule is that the technician app stops keeping its own copy, which is worth
/// nothing if the exported copy can quietly say something else.
/// </summary>
/// <remarks>
/// It parses the emitted text rather than comparing the emitter's input to itself, because the
/// bug being guarded against lives in the rendering — a row dropped, a status misspelt, a
/// terminal state given a successor.
/// </remarks>
[Trait(TestCategories.Name, TestCategories.Unit)]
public sealed partial class JobTransitionParityTests
{
    private static readonly string Emitted = JobTransitionsEmitter.Emit(Job.AllowedTransitions);

    [Fact]
    public void TheEmittedTableSaysExactlyWhatTheDomainTableSays()
    {
        var domain = Job.AllowedTransitions.ToDictionary(
            row => row.Key.ToString(),
            row => Sorted(row.Value.Select(status => status.ToString())),
            StringComparer.Ordinal);

        var emitted = Parse(Emitted);

        Assert.Equal(Sorted(domain.Keys), Sorted(emitted.Keys));

        foreach (var (status, allowed) in domain)
        {
            Assert.Equal(allowed, emitted[status]);
        }
    }

    [Fact]
    public void EveryStatusHasARowIncludingTheOnesNoClientCanDrive()
    {
        var emitted = Parse(Emitted);

        // Completed → Invoiced → Paid are legal and simply not client-initiated, and the
        // terminal states are rows with nowhere to go. A table that omitted either would be
        // lying about the domain in order to keep a client honest.
        Assert.Equal(Sorted(Enum.GetNames<DomainJobStatus>()), Sorted(emitted.Keys));
        Assert.Empty(emitted[nameof(DomainJobStatus.Paid)]);
        Assert.Empty(emitted[nameof(DomainJobStatus.Cancelled)]);
        Assert.Equal([nameof(DomainJobStatus.Invoiced)], emitted[nameof(DomainJobStatus.Completed)]);
    }

    [Fact]
    public void TheHelperHasTheSignatureTheClientsWerePromised()
    {
        Assert.Contains(
            "export function canTransition(from: JobStatus, to: JobStatus): boolean {",
            Emitted,
            StringComparison.Ordinal);
    }

    private static string[] Sorted(IEnumerable<string> names) =>
        [.. names.Order(StringComparer.Ordinal)];

    private static Dictionary<string, string[]> Parse(string typescript) =>
        Row().Matches(typescript).ToDictionary(
            match => match.Groups["from"].Value,
            match => Sorted(match.Groups["to"].Value
                .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .Select(name => name.Trim('\''))),
            StringComparer.Ordinal);

    /// <summary>One row of the emitted table: <c>Scheduled: ['Dispatched', 'Cancelled'],</c>.</summary>
    [GeneratedRegex(@"^  (?<from>\w+): \[(?<to>[^\]]*)\],$", RegexOptions.Multiline)]
    private static partial Regex Row();
}
