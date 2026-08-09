using OpenDispatch.Application.Abstractions;
using OpenDispatch.Domain.Common;
using OpenDispatch.Domain.Identifiers;
using OpenDispatch.TestSupport;

namespace OpenDispatch.Application.Tests.Abstractions;

/// <summary>
/// The ports have no behaviour to test — they are declarations. What they do have is a
/// shape, and the shape carries three decisions that are cheap to lose and expensive to
/// notice: repositories cover aggregate roots and nothing smaller, identifiers are
/// strongly typed, and tenant scope is ambient rather than a parameter every caller has to
/// remember to pass correctly.
/// </summary>
/// <remarks>
/// Each of these fails the day somebody adds a plausible-looking method — an
/// <c>IServiceLocationRepository</c>, a <c>GetAsync(Guid, OrgId, ...)</c> — which is exactly
/// when the decision needs restating. They are not a substitute for the tests that arrive
/// with the implementations in step 28.
/// </remarks>
[Trait(TestCategories.Name, TestCategories.Unit)]
public sealed class PersistencePortTests
{
    private static readonly Type[] Ports = typeof(IUnitOfWork).Assembly
        .GetTypes()
        .Where(type => type.Namespace == typeof(IUnitOfWork).Namespace)
        .ToArray();

    private static readonly Type[] Repositories = Ports
        .Where(type => type.Name.EndsWith("Repository", StringComparison.Ordinal))
        .ToArray();

    [Fact]
    public void TheAbstractionsFolderHoldsPortsAndNoImplementations()
    {
        Assert.NotEmpty(Ports);

        var concrete = Ports.Where(port => !port.IsInterface).Select(port => port.Name).ToArray();

        Assert.True(
            concrete.Length == 0,
            $"An implementation belongs in Infrastructure, not beside the port: {string.Join(", ", concrete)}.");
    }

    [Fact]
    public void EveryRepositoryPortIsNamedForAnAggregateRoot()
    {
        var roots = AggregateRoots().Select(root => root.Name).ToHashSet(StringComparer.Ordinal);

        var misnamed = Repositories
            .Select(repository => repository.Name)
            .Where(name => !roots.Contains(name[1..^"Repository".Length]))
            .ToArray();

        Assert.True(
            misnamed.Length == 0,
            $"Repositories are for aggregate roots only: {string.Join(", ", misnamed)}.");
    }

    [Fact]
    public void NoPortIsAGenericRepositoryOverEverything()
    {
        var generic = Ports.Where(port => port.IsGenericTypeDefinition).Select(port => port.Name).ToArray();

        Assert.True(
            generic.Length == 0,
            $"A repository generic over its entity is a wrapper round the ORM, not a port: {string.Join(", ", generic)}.");
    }

    [Fact]
    public void RepositoriesHandOutWholeAggregatesAndNothingSmaller()
    {
        var domain = typeof(AggregateRoot).Assembly;

        // A service location or a line item reachable on its own would mean two ways into
        // the same data, one of which skips the root that enforces the rules.
        var reachable = Repositories
            .SelectMany(SignatureTypes)
            .Where(type => type.Assembly == domain && type.IsClass && !type.IsAssignableTo(typeof(AggregateRoot)))
            .Select(type => type.Name)
            .Distinct(StringComparer.Ordinal)
            .ToArray();

        Assert.True(
            reachable.Length == 0,
            $"Only aggregate roots cross a repository boundary: {string.Join(", ", reachable)}.");
    }

    [Fact]
    public void PortsSpeakInStronglyTypedIdsAndNeverAskWhoseDataItIs()
    {
        var loose = Ports
            .Where(port => SignatureTypes(port).Any(type => type == typeof(Guid) || type == typeof(OrgId)))
            .Select(port => port.Name)
            .ToArray();

        Assert.True(
            loose.Length == 0,
            "A raw Guid loses which aggregate it identifies, and an OrgId parameter makes tenant "
                + $"scope something each caller can get wrong: {string.Join(", ", loose)}.");
    }

    private static IEnumerable<Type> AggregateRoots() =>
        typeof(AggregateRoot).Assembly
            .GetTypes()
            .Where(type => type is { IsClass: true, IsAbstract: false } && type.IsAssignableTo(typeof(AggregateRoot)));

    /// <summary>
    /// Every type a port's methods traffic in, with wrappers peeled off — so
    /// <c>Task&lt;IReadOnlyList&lt;Job&gt;&gt;</c> is reported as <c>Job</c>.
    /// </summary>
    private static IEnumerable<Type> SignatureTypes(Type port) =>
        port.GetMethods()
            .SelectMany(method => method
                .GetParameters()
                .Select(parameter => parameter.ParameterType)
                .Append(method.ReturnType))
            .SelectMany(Peel);

    private static IEnumerable<Type> Peel(Type type) =>
        type.IsGenericType ? type.GetGenericArguments().SelectMany(Peel) : [type];
}
