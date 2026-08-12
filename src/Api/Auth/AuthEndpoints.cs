using MediatR;
using OpenDispatch.Application.Auth.Login;
using OpenDispatch.Application.Results;
using OpenDispatch.Contracts.Auth;

namespace OpenDispatch.Api.Auth;

/// <summary>
/// <c>POST /auth/login</c> (Document 3, step 44) — the first endpoint in the system, and the
/// only one before step 47's controllers.
/// </summary>
public static class AuthEndpoints
{
    public static IEndpointRouteBuilder MapAuthEndpoints(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapPost("/auth/login", LoginAsync)
            .WithName("Login")
            .AllowAnonymous();

        // TEMPORARY: removed in step 47. Nothing role-guarded exists yet to prove the
        // authentication/authorization pipeline end to end — the real Customers/Technicians/Jobs
        // controllers do that once they land. Until then this is what step 44's own tests call to
        // assert that an anonymous caller is rejected (401) and a wrong-role caller is rejected
        // (403), which no unit test can observe: both are ASP.NET Core middleware behaviour, only
        // visible through a real request against a real host.
        endpoints.MapGet("/auth/_test/admin-only", () => Results.NoContent())
            .RequireAuthorization(AuthPolicies.AdminOnly)
            .ExcludeFromDescription();

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

        if (result.IsSuccess)
        {
            return Results.Ok(new LoginResponse(result.Value.Token, result.Value.ExpiresAt));
        }

        // TEMPORARY: removed in step 46, which builds the shared ErrorCategory -> status code
        // table every controller uses. Login is the only endpoint before it, so a one-off
        // mapping for this handler's two failure shapes is cheaper than building the general
        // table for a single caller.
        return result.Error is ValidationError validation
            ? Results.ValidationProblem(validation.Failures.ToDictionary(
                failure => failure.Key,
                failure => failure.Value.ToArray()))
            : Results.Unauthorized();
    }
}
