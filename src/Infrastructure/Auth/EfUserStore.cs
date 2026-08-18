using Microsoft.EntityFrameworkCore;
using OpenDispatch.Application.Abstractions;
using OpenDispatch.Application.Auth;
using OpenDispatch.Infrastructure.Persistence;

namespace OpenDispatch.Infrastructure.Auth;

/// <inheritdoc cref="IUserStore"/>
/// <remarks>
/// <para>
/// <strong>Users are rows now, not a dictionary.</strong> The store this replaces held them in
/// process memory, which meant every login vanished on restart and a host outside Development had
/// no users at all and no way to acquire any — the single largest gap between this repository and
/// something a shop could run.
/// </para>
/// <para>
/// <strong>Both methods read past the tenant filter, deliberately.</strong> <c>AuthUser</c> carries
/// an <c>OrgId</c>, so the model-wide sweep scopes it like every other tenant-owned table — and
/// signing in is what <em>establishes</em> the tenant, so at that moment there is nothing to scope
/// to and reading <c>ITenantContext.OrgId</c> throws. A username is unique across the deployment
/// (see <c>AuthUserConfiguration</c>) precisely so that this global lookup has exactly one answer.
/// </para>
/// <para>
/// <strong>It saves its own work</strong> rather than staging it for a unit of work, keeping the
/// semantics the in-memory store had. Nothing calls this from inside a command: the callers are the
/// demo registrar at startup and the <c>create-user</c> verb, neither of which is a request and
/// neither of which has a transaction to enlist in.
/// </para>
/// </remarks>
internal sealed class EfUserStore(AppDbContext context) : IUserStore
{
    public async Task<AuthUser?> FindByUsernameAsync(string username, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(username);

        var normalized = AuthUsername.Normalize(username);

        return await context.Users
            .IgnoreQueryFilters()
            .AsNoTracking()
            .FirstOrDefaultAsync(user => user.Username == normalized, ct)
            .ConfigureAwait(false);
    }

    /// <remarks>
    /// An upsert, because that is what the port promises ("adds a user, or replaces the one already
    /// seeded under this username") and because the demo registrar runs on every Development start:
    /// an insert-only store would trip the unique index the second time a developer pressed F5. The
    /// existing row keeps its own identity — a re-seeded login is the same user with a new password,
    /// not a new user wearing the old name.
    /// </remarks>
    public async Task AddAsync(AuthUser user, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(user);

        var normalized = user with { Username = AuthUsername.Normalize(user.Username) };

        var existing = await context.Users
            .IgnoreQueryFilters()
            .FirstOrDefaultAsync(candidate => candidate.Username == normalized.Username, ct)
            .ConfigureAwait(false);

        if (existing is null)
        {
            context.Users.Add(normalized);
        }
        else
        {
            context.Entry(existing).CurrentValues.SetValues(normalized with { Id = existing.Id });
        }

        await context.SaveChangesAsync(ct).ConfigureAwait(false);
    }

    /// <remarks>
    /// Past the tenant filter like its neighbours, and for the same reason: this runs from a verb
    /// rather than a request, so there is no ambient tenant to scope to — and a username is unique
    /// across the deployment, so naming one is naming exactly one row.
    /// </remarks>
    public async Task<bool> SetActiveAsync(string username, bool active, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(username);

        var normalized = AuthUsername.Normalize(username);

        var existing = await context.Users
            .IgnoreQueryFilters()
            .FirstOrDefaultAsync(candidate => candidate.Username == normalized, ct)
            .ConfigureAwait(false);

        if (existing is null)
        {
            return false;
        }

        context.Entry(existing).CurrentValues.SetValues(existing with { IsActive = active });

        await context.SaveChangesAsync(ct).ConfigureAwait(false);

        return true;
    }
}
