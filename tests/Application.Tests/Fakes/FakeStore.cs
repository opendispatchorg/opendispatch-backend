using Microsoft.Extensions.DependencyInjection;
using OpenDispatch.Domain.Identifiers;

namespace OpenDispatch.Application.Tests.Fakes;

/// <summary>Somewhere staged work can be written or thrown away.</summary>
internal interface IStagedWrites
{
    /// <summary>Writes what has been staged.</summary>
    void Write();

    /// <summary>Throws away what has been staged and not written.</summary>
    void Discard();
}

/// <summary>
/// The aggregates a fake database holds, and the ones a fake transaction has not written yet.
/// </summary>
/// <typeparam name="TAggregate">The aggregate root being stored.</typeparam>
/// <remarks>
/// <para>
/// The split between staged and written is what makes this worth having rather than a list. The
/// real ports stage and save separately — a repository never writes, and a command's work becomes
/// real when the pipeline commits — so a store that made an added aggregate immediately readable
/// would let a pipeline that simply forgot to save pass every test here.
/// </para>
/// <para>
/// What it cannot model is a change to something already written: the aggregate is one object and
/// a handler mutates it, so a rolled-back skill change would still show here. That claim is about
/// a database, and is proved against Postgres in <c>Api.IntegrationTests</c>.
/// </para>
/// </remarks>
/// <param name="tenantOf">Which organization an aggregate belongs to.</param>
internal sealed class FakeStore<TAggregate>(Func<TAggregate, OrgId> tenantOf) : IStagedWrites
{
    private readonly List<TAggregate> _written = [];
    private readonly List<TAggregate> _staged = [];

    /// <summary>Everything written, whichever tenant it belongs to.</summary>
    public IReadOnlyList<TAggregate> Saved => _written;

    /// <summary>Stages an aggregate, unwritten until something saves.</summary>
    public void Stage(TAggregate aggregate) => _staged.Add(aggregate);

    public void Write()
    {
        _written.AddRange(_staged);
        _staged.Clear();
    }

    public void Discard() => _staged.Clear();

    /// <summary>What one tenant can see — the fake's version of the global query filter.</summary>
    public IEnumerable<TAggregate> Owned(OrgId tenant) =>
        _written.Where(aggregate => tenantOf(aggregate) == tenant);
}

/// <summary>Registers a store so both the repository and the unit of work can reach it.</summary>
internal static class FakeStoreRegistration
{
    /// <summary>Adds a store for one aggregate root.</summary>
    /// <typeparam name="TAggregate">The aggregate root being stored.</typeparam>
    /// <param name="services">The container being built.</param>
    /// <param name="tenantOf">Which organization an aggregate belongs to.</param>
    public static IServiceCollection AddStore<TAggregate>(
        this IServiceCollection services,
        Func<TAggregate, OrgId> tenantOf) =>
        services
            .AddSingleton(new FakeStore<TAggregate>(tenantOf))
            .AddSingleton<IStagedWrites>(provider => provider.GetRequiredService<FakeStore<TAggregate>>());
}
