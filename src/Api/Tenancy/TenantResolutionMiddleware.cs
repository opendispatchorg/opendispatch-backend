using System.IdentityModel.Tokens.Jwt;
using OpenDispatch.Application.Abstractions;
using OpenDispatch.Application.Auth;
using OpenDispatch.Domain.Identifiers;
using OpenDispatch.Infrastructure.Auth;
using OpenDispatch.Infrastructure.Tenancy;

namespace OpenDispatch.Api.Tenancy;

/// <summary>
/// Reads the caller's org claim and resolves the request's tenant from it
/// — the one thing that has to happen before a handler can touch tenant-scoped data, since
/// <c>TenantContext.OrgId</c> throws until something calls <c>Resolve</c> (Document 2 §7/§8,
/// step 45).
/// </summary>
/// <remarks>
/// <para>
/// Runs after <c>UseAuthentication</c>/<c>UseAuthorization</c> (Program.cs), so by the time this
/// middleware sees a request it is one of exactly two shapes: genuinely anonymous, on an
/// <c>AllowAnonymous</c> endpoint — <c>/health</c>, <c>/auth/login</c> — with nothing to resolve
/// and nothing to reject, because a missing or invalid token was already rejected upstream; or
/// carrying a validated principal, in which case every token this system issues carries an org
/// claim (<see cref="AuthClaimTypes.Org"/>), so a validated principal with none, or one that does
/// not parse, means a token this system did not issue in the ordinary way. That is new territory
/// neither middleware above checks — the reason this one exists — and it is rejected here.
/// </para>
/// <para>
/// <see cref="ITenantScope"/> is injected into <see cref="InvokeAsync"/>, not the constructor:
/// the constructor runs once at startup, but the tenant context is scoped to a request, and only
/// parameters resolved per-invocation get the request's own scope.
/// </para>
/// </remarks>
public sealed class TenantResolutionMiddleware(RequestDelegate next)
{
    public async Task InvokeAsync(HttpContext context, ITenantScope tenant, CallerContext caller)
    {
        if (context.User.Identity?.IsAuthenticated == true)
        {
            var claim = context.User.FindFirst(AuthClaimTypes.Org)?.Value;

            if (claim is null || !Guid.TryParse(claim, out var orgId))
            {
                // No ProblemDetails body: this is a raw pipeline rejection ahead of the
                // Result/Error machinery that step 46 centralizes, in the same spirit as the
                // bare 401/403 UseAuthentication/UseAuthorization already answer with for their
                // own failures — this is one more shape of the same kind of rejection, not a
                // handler's answer to a well-formed question.
                context.Response.StatusCode = StatusCodes.Status401Unauthorized;
                return;
            }

            tenant.Resolve(OrgId.From(orgId));

            // And who they are, from the same principal, for the audit trail. Unlike the org claim
            // this is not required: a token without a subject is a token this system did not issue
            // in the usual way, and the trail says "nobody" rather than the request being refused —
            // the tenant is what protects data, and it has already been established above.
            if (Guid.TryParse(context.User.FindFirst(JwtRegisteredClaimNames.Sub)?.Value, out var userId))
            {
                caller.Resolve(
                    UserId.From(userId),
                    context.User.FindFirst(JwtRegisteredClaimNames.UniqueName)?.Value);
            }
        }

        await next(context);
    }
}

/// <summary>Registration for <see cref="TenantResolutionMiddleware"/>.</summary>
public static class TenantResolutionMiddlewareExtensions
{
    public static IApplicationBuilder UseTenantResolution(this IApplicationBuilder app) =>
        app.UseMiddleware<TenantResolutionMiddleware>();
}
