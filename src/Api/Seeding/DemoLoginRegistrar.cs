using System.Data.Common;
using OpenDispatch.Infrastructure.Seeding;

namespace OpenDispatch.Api.Seeding;

/// <summary>
/// Puts the demo logins into this host's user store as it starts.
/// </summary>
/// <remarks>
/// <para>
/// The demo logins are established here rather than by <c>make seed</c>. That began as a necessity
/// — the user store was an in-memory singleton, so a login written by the seeding process died with
/// it — and the necessity is gone: the store is <c>EfUserStore</c> over a real <c>users</c> table,
/// so <c>make seed</c> could write them now.
/// </para>
/// <para>
/// It stays here anyway, for a different and better reason: a demo host must be able to serve its
/// own logins whether or not anybody ran the seeder in this container, and re-establishing them on
/// every start is what makes a demo reproducible after somebody has changed a password poking at
/// it. What it does write goes to the database like any other login, so nothing dies with a
/// process any more.
/// </para>
/// <para>
/// Registered only in Development, alongside <see cref="DemoSeeder"/> itself. A production host
/// neither has this service nor could resolve what it depends on.
/// </para>
/// <para>
/// An unreachable or unmigrated database is a warning, not a failed start: <c>make run</c> before
/// <c>make up</c> is an ordinary morning, and a host that refused to boot over the demo logins
/// would be a worse answer than one that boots without them and says so.
/// </para>
/// <para>
/// What that costs is the exception filter below rather than a plain <c>catch</c>, and the reason
/// is worth stating because the obvious version does not work: EF Core's execution strategy wraps a
/// refused connection in an <see cref="InvalidOperationException"/> ("likely due to a transient
/// failure"), so catching <see cref="DbException"/> alone lets a dead database take the host down
/// with it — verified by running against a closed port. An unmigrated one arrives as a bare
/// <c>PostgresException</c> instead, so both shapes have to be recognised, and recognising them by
/// cause rather than by outer type is what keeps this from swallowing an ordinary bug.
/// </para>
/// </remarks>
internal sealed class DemoLoginRegistrar(
    IServiceScopeFactory scopes,
    ILogger<DemoLoginRegistrar> logger) : IHostedService
{
    public async Task StartAsync(CancellationToken cancellationToken)
    {
        await using var scope = scopes.CreateAsyncScope();
        var seeder = scope.ServiceProvider.GetRequiredService<DemoSeeder>();

        try
        {
            var registered = await seeder.RegisterLoginsAsync(cancellationToken).ConfigureAwait(false);

            if (registered == 0)
            {
                SeedLog.NothingToSignInTo(logger);
            }
            else
            {
                SeedLog.LoginsRegistered(logger, registered);
            }
        }
        catch (Exception exception) when (CausedByTheDatabase(exception))
        {
            SeedLog.LoginsUnavailable(logger, exception);
        }
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    /// <summary>Whether this failure came from the database rather than from this code.</summary>
    private static bool CausedByTheDatabase(Exception exception) =>
        exception is DbException
        || (exception.InnerException is { } inner && CausedByTheDatabase(inner));
}
