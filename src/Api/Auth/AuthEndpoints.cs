using MediatR;
using OpenDispatch.Api.ErrorHandling;
using OpenDispatch.Application.Auth.Login;
using OpenDispatch.Contracts.Auth;

namespace OpenDispatch.Api.Auth;

/// <summary>
/// <c>POST /auth/login</c> (Document 3, step 44) — the first endpoint in the system.
/// </summary>
public static class AuthEndpoints
{
    public static IEndpointRouteBuilder MapAuthEndpoints(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapPost("/auth/login", LoginAsync)
            .WithName("Login")
            .AllowAnonymous()
            .Produces<LoginResponse>();

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
