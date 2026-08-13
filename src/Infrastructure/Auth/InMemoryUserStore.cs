using System.Collections.Concurrent;
using OpenDispatch.Application.Abstractions;
using OpenDispatch.Application.Auth;

namespace OpenDispatch.Infrastructure.Auth;

/// <summary>
/// <see cref="IUserStore"/> over a process-lifetime dictionary — "a minimal user store (can be
/// seeded)" (Document 3, step 44), and the whole of it.
/// </summary>
/// <remarks>
/// <para>
/// Not backed by Postgres, deliberately: the build plan names no endpoint or migration for
/// users anywhere, only a store a caller can seed and log in against. Persisting one is a
/// question for whichever future step first needs a user to survive a restart — a real
/// deployment today seeds its users the same way <c>make seed</c> (step 53) seeds everything
/// else, from a composition root that runs once at startup.
/// </para>
/// <para>
/// A singleton, like <c>FakePaymentGateway</c> and the local-disk attachment store: it holds
/// state (unlike those two) but no connection, and the whole point is that it survives across
/// requests within one process without a database behind it.
/// </para>
/// </remarks>
internal sealed class InMemoryUserStore : IUserStore
{
    private readonly ConcurrentDictionary<string, AuthUser> _byUsername = new(StringComparer.OrdinalIgnoreCase);

    public Task<AuthUser?> FindByUsernameAsync(string username, CancellationToken ct) =>
        Task.FromResult(_byUsername.GetValueOrDefault(username));

    public Task AddAsync(AuthUser user, CancellationToken ct)
    {
        _byUsername[user.Username] = user;
        return Task.CompletedTask;
    }
}
