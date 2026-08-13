using OpenDispatch.Application.Results;

namespace OpenDispatch.Application.Auth;

/// <summary>The one failure a login can report.</summary>
/// <remarks>
/// One message for "no such user" and one for "wrong password" would tell a caller which
/// usernames exist on this system, one probe at a time — the same reasoning
/// <c>CustomerErrors.NotFound</c> gives for not distinguishing "never existed" from "belongs to
/// somebody else". Both fail identically here, on purpose.
/// </remarks>
public static class AuthErrors
{
    /// <summary>The code every failed login carries.</summary>
    public const string InvalidCredentialsCode = "auth.invalidCredentials";

    /// <summary>The username is not on this system, or the password does not match it.</summary>
    public static Error InvalidCredentials { get; } =
        Error.Unauthorized(InvalidCredentialsCode, "That username or password is not recognised.");
}
