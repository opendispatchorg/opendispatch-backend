using OpenDispatch.Application.Abstractions;
using OpenDispatch.Domain.Common;
using OpenDispatch.Domain.Identifiers;
using OpenDispatch.TestSupport;

namespace OpenDispatch.Application.Tests.Abstractions;

/// <summary>
/// The ports have no behaviour to test — they are declarations. What they do have is a
/// shape, and the shape carries decisions that are cheap to lose and expensive to notice:
/// repositories cover aggregate roots and nothing smaller, the board is a projection rather
/// than a view onto aggregates, identifiers are strongly typed, and tenant scope is ambient
/// rather than a parameter every caller has to remember to pass correctly.
/// </summary>
/// <remarks>
/// Each of these fails the day somebody adds a plausible-looking method — an
/// <c>IServiceLocationRepository</c>, a <c>GetAsync(Guid, OrgId, ...)</c>, a <c>Job</c> on
/// the board — which is exactly when the decision needs restating. They are not a substitute
/// for the tests that arrive with the implementations in steps 28 and 39.
/// </remarks>
[Trait(TestCategories.Name, TestCategories.Unit)]
public sealed class PortTests
{
    private static readonly Type[] Abstractions = typeof(IUnitOfWork).Assembly
        .GetTypes()
        .Where(type => type.Namespace == typeof(IUnitOfWork).Namespace)
        .ToArray();

    private static readonly Type[] Ports = Abstractions.Where(type => type.IsInterface).ToArray();

    private static readonly Type[] Repositories = Ports
        .Where(type => type.Name.EndsWith("Repository", StringComparison.Ordinal))
        .ToArray();

    [Fact]
    public void NothingBesideAPortImplementsIt()
    {
        Assert.NotEmpty(Ports);

        // The types that are not interfaces are the payloads the ports traffic in — a board
        // snapshot, a payment result. An adapter is a different thing and belongs in
        // Infrastructure, where it can depend on the world.
        var implementations = Abstractions
            .Where(type => !type.IsInterface && Array.Exists(Ports, port => type.IsAssignableTo(port)))
            .Select(type => type.Name)
            .ToArray();

        Assert.True(
            implementations.Length == 0,
            $"An implementation belongs in Infrastructure, not beside the port: {string.Join(", ", implementations)}.");
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
    public void OnlyRepositoriesTrafficInAggregates()
    {
        // The board reads a projection. An aggregate on it would be a second way to reach
        // data that the repositories exist to guard — and, since the caller of a read model
        // has no unit of work, one that cannot save what it changes.
        var offenders = Abstractions
            .Except(Repositories)
            .Where(type => SignatureTypes(type).Any(used => used.IsAssignableTo(typeof(AggregateRoot))))
            .Select(type => type.Name)
            .ToArray();

        Assert.True(
            offenders.Length == 0,
            $"Everything but a repository speaks in projections and value objects: {string.Join(", ", offenders)}.");
    }

    [Fact]
    public void PortsSpeakInStronglyTypedIdsAndNeverAskWhoseDataItIs()
    {
        var loose = Abstractions
            .Where(type => type != typeof(ITenantContext) && type != typeof(ITenantScope))
            .Where(type => SignatureTypes(type).Any(used => used == typeof(Guid) || used == typeof(OrgId)))
            .Select(type => type.Name)
            .ToArray();

        Assert.True(
            loose.Length == 0,
            "A raw Guid loses which aggregate it identifies, and an OrgId parameter makes tenant "
                + $"scope something each caller can get wrong: {string.Join(", ", loose)}.");
    }

    /// <summary>
    /// The exemption above, stated as its own rule so it cannot quietly widen.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Two ports may mention an <see cref="OrgId"/>, and between them they are the whole of how
    /// tenant scope exists: <see cref="ITenantContext"/> hands it out, making scope ambient for
    /// everything else, and <see cref="ITenantScope"/> establishes it. A third would mean scope had
    /// become a parameter again somewhere, which is what the rule above exists to prevent; and if
    /// either stops mentioning one, the ambient scope has lost either its source or its reader.
    /// </para>
    /// <para>
    /// <strong>The write half was split out rather than added to <c>ITenantContext</c></strong> so
    /// that reading the tenant and deciding it stay different capabilities — the hundred-odd places
    /// that take <c>ITenantContext</c> still cannot set one. Only two things resolve: the API's
    /// tenant middleware, from an authenticated principal, and the outbox dispatcher, from the owner
    /// recorded on each message. The second is why this port exists at all; without it the
    /// background sweep had no tenant, and every reaction it touched threw and became a poison row.
    /// </para>
    /// </remarks>
    [Fact]
    public void ExactlyTwoPortsSayWhoseDataItIs()
    {
        var suppliers = Abstractions
            .Where(type => SignatureTypes(type).Any(used => used == typeof(OrgId)))
            .OrderBy(type => type.Name, StringComparer.Ordinal)
            .ToArray();

        Assert.Equal([typeof(ITenantContext), typeof(ITenantScope)], suppliers);
    }

    private static IEnumerable<Type> AggregateRoots() =>
        typeof(AggregateRoot).Assembly
            .GetTypes()
            .Where(type => type is { IsClass: true, IsAbstract: false } && type.IsAssignableTo(typeof(AggregateRoot)));

    /// <summary>
    /// Every type a port's methods traffic in, with wrappers peeled off — so
    /// <c>Task&lt;IReadOnlyList&lt;Job&gt;&gt;</c> is reported as <c>Job</c>. Over a payload
    /// rather than a port, the property getters make this the fields it carries.
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
