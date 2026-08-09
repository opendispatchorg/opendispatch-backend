using OpenDispatch.Contracts.Sync;
using OpenDispatch.TestSupport;
using DomainJobStatus = OpenDispatch.Domain.Jobs.JobStatus;

namespace OpenDispatch.Contracts.Tests;

/// <summary>
/// Two halves of one decision. The wire vocabulary is a copy of the domain's because
/// Contracts depends on nothing (Document 2 §2) — and a copy nobody checks is a copy that
/// drifts, quietly, in the direction of a client that has never heard of a status the server
/// now sends.
/// </summary>
/// <remarks>
/// The pair fails in opposite directions on purpose: one when somebody adds a status to the
/// domain and stops there, the other when somebody notices the duplication and "fixes" it
/// with a project reference. The second is the one worth catching, because it compiles.
/// </remarks>
[Trait(TestCategories.Name, TestCategories.Unit)]
public sealed class ContractsAssemblyTests
{
    [Fact]
    public void WireJobStatusMirrorsTheDomainNameForNameAndNumberForNumber()
    {
        var domain = Enum.GetValues<DomainJobStatus>()
            .Select(status => (Name: status.ToString(), Value: (int)status))
            .ToHashSet();

        var wire = Enum.GetValues<JobStatus>()
            .Select(status => (Name: status.ToString(), Value: (int)status))
            .ToHashSet();

        var onlyInDomain = domain.Except(wire).ToArray();
        var onlyOnTheWire = wire.Except(domain).ToArray();

        Assert.True(
            onlyInDomain.Length == 0 && onlyOnTheWire.Length == 0,
            $"The clients would not know about [{Describe(onlyInDomain)}], and the server means "
                + $"nothing by [{Describe(onlyOnTheWire)}].");
    }

    [Fact]
    public void ContractsCarriesNothingElseOfOursIntoTheClients()
    {
        var ours = typeof(SyncOp).Assembly
            .GetReferencedAssemblies()
            .Select(assembly => assembly.Name ?? string.Empty)
            .Where(name => name.StartsWith("OpenDispatch.", StringComparison.Ordinal))
            .ToArray();

        Assert.True(
            ours.Length == 0,
            "Contracts is the one assembly the clients see, so anything it references is "
                + $"something they see too: {string.Join(", ", ours)}.");
    }

    private static string Describe(IEnumerable<(string Name, int Value)> statuses) =>
        string.Join(", ", statuses.Select(status => $"{status.Name} = {status.Value}"));
}
