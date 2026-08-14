using MediatR;
using OpenDispatch.Api.ErrorHandling;
using OpenDispatch.Api.Security;
using OpenDispatch.Application.Auth.Login;
using OpenDispatch.Contracts.Auth;

namespace OpenDispatch.Api.Auth;

/// <summary>
/// <c>POST /auth/login</c> (Document 3, step 44) — the first endpoint in the system.
/// </summary>
/// <remarks>
/// The only rate-limited route in this API, and the only one that needs to be: it is the single
/// place an anonymous caller can make this host do expensive work (a deliberately slow password
/// verification) and the single place a guess could be worth anything. See
/// <see cref="RateLimiting"/>.
/// </remarks>
public static class AuthEndpoints
{
    public static IEndpointRouteBuilder MapAuthEndpoints(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapPost("/auth/login", LoginAsync)
            .WithName("Login")
            .AllowAnonymous()
            .RequireRateLimiting(RateLimiting.LoginPolicy)
            .Produces<LoginResponse>()
            .Produces(StatusCodes.Status429TooManyRequests);

        return endpoints;
    }

    private static async Task<IResult> LoginAsync(
        LoginRequest request,
        ISender sender,
        CancellationToken cancellationToken)
    {
        var result = await sender
            .Send(new LoginQuery(request.Username, request.Password), cancellationToken)
            .ConfigureAwait(false);

        return result.ToHttpResult(token => Results.Ok(new LoginResponse(token.Token, token.ExpiresAt)));
    }
}
