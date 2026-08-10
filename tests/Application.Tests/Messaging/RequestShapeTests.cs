using OpenDispatch.Application.Messaging;
using OpenDispatch.Domain.Identifiers;
using OpenDispatch.TestSupport;

namespace OpenDispatch.Application.Tests.Messaging;

/// <summary>
/// What a command or a query is allowed to carry, enforced over every request in the application
/// layer rather than remembered slice by slice.
/// </summary>
/// <remarks>
/// <para>
/// The first rule is the one that matters, and it guards an exposure the persistence layer
/// structurally cannot: EF's global query filters scope every <em>read</em> to the tenant, so a
/// row filed under the wrong organization is not a leak — it is a row that vanishes from the
/// tenant that created it and appears for one that did not. Nothing on the read path would notice.
/// </para>
/// <para>
/// So the rule is that a handler takes the organization from <c>ITenantContext</c> and never from
/// its request, and this is what holds it: a request that cannot name an organization cannot be
/// handled by a handler that trusts one. It fails the day somebody adds a plausible-looking
/// <c>OrgId</c> to a command — which is exactly when the decision needs restating, and three
/// slices later it will be restated by copying whichever slice was opened first.
/// </para>
/// <para>
/// The same shape of check as <c>PortTests</c>, for the same reason: these are decisions that are
/// cheap to lose and expensive to notice.
/// </para>
/// </remarks>
[Trait(TestCategories.Name, TestCategories.Unit)]
public sealed class RequestShapeTests
{
    private static readonly Type[] Requests = typeof(ApplicationRegistration).Assembly
        .GetTypes()
        .Where(type => type is { IsClass: true, IsAbstract: false })
        .Where(type => type.IsAssignableTo(typeof(ICommandBase)) || IsQuery(type))
        .ToArray();

    [Fact]
    public void NoCommandOrQuerySaysWhoseDataItIs()
    {
        Assert.NotEmpty(Requests);

        var offenders = Requests
            .Where(request => Carries(request).Any(type => type == typeof(OrgId)))
            .Select(request => request.Name)
            .ToArray();

        Assert.True(
            offenders.Length == 0,
            "A handler takes the tenant from ITenantContext, never from the request: "
                + $"{string.Join(", ", offenders)}.");
    }

    /// <summary>
    /// A raw <see cref="Guid"/> on a request is the other half of the same mistake: it loses which
    /// aggregate it identifies, so the value that arrives is whichever one the caller happened to
    /// have — and a job pointed at a customer id would compile.
    /// </summary>
    [Fact]
    public void EveryIdentifierOnARequestIsStronglyTyped()
    {
        Assert.NotEmpty(Requests);

        var offenders = Requests
            .Where(request => Carries(request).Any(type => type == typeof(Guid)))
            .Select(request => request.Name)
            .ToArray();

        Assert.True(
            offenders.Length == 0,
            $"A raw Guid loses which aggregate it identifies: {string.Join(", ", offenders)}.");
    }

    private static bool IsQuery(Type type) =>
        Array.Exists(
            type.GetInterfaces(),
            contract => contract.IsGenericType
                && contract.GetGenericTypeDefinition() == typeof(IQuery<>));

    /// <summary>
    /// The types a request holds, with <see cref="Nullable{T}"/> peeled off so an optional
    /// identifier is judged as the thing it is optional about.
    /// </summary>
    private static IEnumerable<Type> Carries(Type request) =>
        request.GetProperties()
            .Select(property => Nullable.GetUnderlyingType(property.PropertyType) ?? property.PropertyType);
}
