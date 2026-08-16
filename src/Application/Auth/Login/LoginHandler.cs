using MediatR;
using OpenDispatch.Application.Abstractions;
using OpenDispatch.Application.Results;

namespace OpenDispatch.Application.Auth.Login;

/// <summary>Checks a password and, if it matches, issues a token.</summary>
/// <remarks>
/// No <see cref="ITenantContext"/> here, and that is deliberate rather than an oversight: this
/// is how a caller gets a tenant in the first place, so there is no ambient scope yet for this
/// one request to read. The organization comes back from the store, on the user it found.
/// </remarks>
internal sealed class LoginHandler(IUserStore users, IPasswordHasher hasher, ITokenIssuer tokens)
    : IRequestHandler<LoginQuery, Result<AuthToken>>
{
    public async Task<Result<AuthToken>> Handle(LoginQuery query, CancellationToken cancellationToken)
    {
        var user = await users.FindByUsernameAsync(query.Username, cancellationToken).ConfigureAwait(false);

        // Three branches now, and still one answer. A disabled login joins the other two because
        // telling somebody their account exists but is switched off is telling somebody who is not
        // them the same thing — and the password is still checked either way, so the two paths take
        // the same time. See AuthErrors.
        if (user is null || !user.IsActive || !hasher.Verify(query.Password, user.PasswordHash))
        {
            return Result.Failure<AuthToken>(AuthErrors.InvalidCredentials);
        }

        return Result.Success(tokens.Issue(user));
    }
}
