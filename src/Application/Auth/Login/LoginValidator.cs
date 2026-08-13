using FluentValidation;

namespace OpenDispatch.Application.Auth.Login;

/// <summary>Refuses a login nobody could possibly match, before a lookup runs for it.</summary>
internal sealed class LoginValidator : AbstractValidator<LoginQuery>
{
    public LoginValidator()
    {
        RuleFor(query => query.Username).NotEmpty();
        RuleFor(query => query.Password).NotEmpty();
    }
}
