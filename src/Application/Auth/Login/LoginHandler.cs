using MediatR;
using OpenDispatch.Application.Abstractions;
using OpenDispatch.Application.Observability;
using OpenDispatch.Application.Results;

namespace OpenDispatch.Application.Auth.Login;

/// <summary>Checks a password and, if it matches, issues a token.</summary>
/// <remarks>
/// No <see cref="ITenantContext"/> here, and that is deliberate rather than an oversight: this
/// is how a caller gets a tenant in the first place, so there is no ambient scope yet for this
/// one request to read. The organization comes back from the store, on the user it found.
/// </remarks>
internal sealed class LoginHandler(
    IUserStore users,
    IPasswordHasher hasher,
    ITokenIssuer tokens,
    AuthMetrics metrics)
    : IRequestHandler<LoginQuery, Result<AuthToken>>
{
    public async Task<Result<AuthToken>> Handle(LoginQuery query, CancellationToken cancellationToken)
    {
        var user = await users.FindByUsernameAsync(query.Username, cancellationToken).ConfigureAwait(false);

        // Three branches, and still one answer. A disabled login joins the other two because telling
        // somebody their account exists but is switched off is telling somebody who is not them the
        // same thing — and the password is still checked either way, so the two paths take the same
        // time. See AuthErrors.
        //
        // The reason is counted rather than returned. Nothing reaches the caller that the single
        // error did not already say; what changes is that a server-side alert can now tell a
        // password list being worked through from somebody who left still trying their old login.
        // Until this, repeated failed sign-ins were the one thing in the system nothing recorded —
        // the audit trail only holds commands that changed something, deliberately.
        // Split from one `||` chain into three branches purely so each can be counted. The order,
        // the short-circuiting and therefore the timing are exactly what they were — an unknown
        // username still returns without hashing anything, as it did before.
        if (user is null)
        {
            metrics.SignInFailed(AuthMetrics.UnknownUser);

            return Result.Failure<AuthToken>(AuthErrors.InvalidCredentials);
        }

        if (!user.IsActive)
        {
            metrics.SignInFailed(AuthMetrics.DisabledUser);

            return Result.Failure<AuthToken>(AuthErrors.InvalidCredentials);
        }

        if (!hasher.Verify(query.Password, user.PasswordHash))
        {
            metrics.SignInFailed(AuthMetrics.WrongPassword);

            return Result.Failure<AuthToken>(AuthErrors.InvalidCredentials);
        }

        return Result.Success(tokens.Issue(user));
    }
}
