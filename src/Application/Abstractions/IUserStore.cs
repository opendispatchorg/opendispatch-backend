using OpenDispatch.Application.Auth;

namespace OpenDispatch.Application.Abstractions;

/// <summary>
/// The minimal user store step 44 asks for.
/// </summary>
/// <remarks>
/// Not an <c>IUserRepository</c>: <see cref="AuthUser"/> is not an aggregate root (see
/// <see cref="UserRole"/>'s remarks on why it is not in the domain at all), so this is named
/// and shaped like <c>ISyncOpStore</c> rather than the repository ports beside it.
/// </remarks>
public interface IUserStore
{
    /// <summary>
    /// Finds the user with this username, case-insensitively, or <see langword="null"/> if
    /// nobody by that name has been seeded.
    /// </summary>
    Task<AuthUser?> FindByUsernameAsync(string username, CancellationToken ct);

    /// <summary>
    /// Adds a user, or replaces the one already seeded under this username. What "can be
    /// seeded" (Document 3, step 44) means for this store.
    /// </summary>
    Task AddAsync(AuthUser user, CancellationToken ct);

    /// <summary>
    /// Switches a login on or off, and says whether there was one to switch.
    /// </summary>
    /// <param name="username">Whose login, case-insensitively.</param>
    /// <param name="active">Whether they may sign in.</param>
    /// <param name="ct">Cancellation.</param>
    /// <returns><see langword="false"/> if no login has that username.</returns>
    /// <remarks>
    /// Deliberately not a delete: the audit trail names a user by id, and a deleted row would turn
    /// every act they ever took into an entry pointing at nobody. Somebody who has left the shop
    /// stops being able to sign in and goes on being the answer to "who cancelled this job".
    /// </remarks>
    Task<bool> SetActiveAsync(string username, bool active, CancellationToken ct);
}
