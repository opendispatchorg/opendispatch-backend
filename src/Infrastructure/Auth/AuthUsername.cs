using System.Globalization;

namespace OpenDispatch.Infrastructure.Auth;

/// <summary>
/// The one rule for turning what somebody typed into what is stored and compared.
/// </summary>
/// <remarks>
/// <para>
/// <c>IUserStore</c> promises a case-insensitive lookup, and the store that keeps its promise with
/// a database rather than a dictionary has to say how: a unique index compares bytes, so "Ada@…"
/// and "ada@…" are two rows unless something makes them one. Normalizing on the way in makes the
/// index that enforces uniqueness the same index the lookup uses.
/// </para>
/// <para>
/// Invariant lowercase, not the host's culture: a deployment in Turkey would otherwise store
/// "ADA" as "ada" on one machine and "adı" on another, and a login would work on one host and fail
/// on the next. Trimmed too, because a username pasted with a trailing space is the same person.
/// </para>
/// </remarks>
internal static class AuthUsername
{
    /// <summary>What a username is stored and looked up as.</summary>
    /// <param name="username">What was typed, or what was seeded.</param>
    public static string Normalize(string username) =>
        username.Trim().ToLower(CultureInfo.InvariantCulture);
}
