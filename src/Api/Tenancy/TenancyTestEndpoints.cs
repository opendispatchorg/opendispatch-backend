using OpenDispatch.Application.Abstractions;

namespace OpenDispatch.Api.Tenancy;

/// <summary>
/// <c>GET /tenant/_test/my-customers</c> (Document 3, step 45) — proves tenant resolution and
/// the EF global query filters (step 29) end to end, over a real HTTP request.
/// </summary>
public static class TenancyTestEndpoints
{
    public static IEndpointRouteBuilder MapTenancyTestEndpoints(this IEndpointRouteBuilder endpoints)
    {
        // TEMPORARY: removed in step 47. Nothing tenant-scoped is reachable over HTTP yet — the
        // real Customers controller is what a production deployment would actually read through.
        // Until then this is what step 45's own test calls to prove that a caller under one
        // organization cannot read another's rows through a real request: authenticate, resolve
        // the org claim into ITenantContext (TenantResolutionMiddleware), read through the
        // existing (step 28) repository, and let the query filter do what it always does.
        endpoints.MapGet("/tenant/_test/my-customers", async (ICustomerRepository customers, CancellationToken ct) =>
            {
                var mine = await customers.ListAsync(ct).ConfigureAwait(false);

                return Results.Ok(mine.Select(customer => customer.Name));
            })
            .RequireAuthorization()
            .ExcludeFromDescription();

        return endpoints;
    }
}
