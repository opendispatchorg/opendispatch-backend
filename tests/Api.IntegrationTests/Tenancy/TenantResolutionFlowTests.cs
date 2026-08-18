using System.IdentityModel.Tokens.Jwt;
using System.Net;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Text;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;
using OpenDispatch.Api.Configuration;
using OpenDispatch.Api.IntegrationTests.Fixtures;
using OpenDispatch.Application.Auth;
using OpenDispatch.Contracts.Customers;
using OpenDispatch.Domain.Identifiers;
using OpenDispatch.Infrastructure.Auth;
using OpenDispatch.TestSupport;
using OpenDispatch.TestSupport.Builders;

namespace OpenDispatch.Api.IntegrationTests.Tenancy;

/// <summary>
/// Tenant resolution against a real host and a real database — the test the whole step exists
/// for (Document 3, step 45): a request under one organization cannot read another's data
/// through a real endpoint.
/// </summary>
/// <remarks>
/// The unit half of this story — that a filter scopes a query — already has its own suite
/// (<c>TenantIsolationTests</c>, step 29). What only a real host adds is
/// <c>TenantResolutionMiddleware</c> itself: whether the org claim on a real bearer token
/// actually reaches <c>ITenantContext</c> before a handler runs, and what happens when it does
/// not.
/// </remarks>
[Collection(PostgresCollectionDefinition.Name)]
[Trait(TestCategories.Name, TestCategories.Integration)]
public sealed class TenantResolutionFlowTests : IClassFixture<ApiFactory>
{
    private readonly ApiFactory _factory;
    private readonly PostgresFixture _postgres;

    public TenantResolutionFlowTests(ApiFactory factory, PostgresFixture postgres)
    {
        _factory = factory;
        _postgres = postgres;

        // Every test in this class ends up making a real HTTP call, and ApiFactory only reads
        // ConnectionString the first time something builds the host — so it has to be set before
        // any of them, not just the ones that seed data.
        _factory.ConnectionString = postgres.ConnectionString;
    }

    [Fact]
    public async Task ARequestUnderOneOrgCannotReadAnothersDataThroughARealEndpoint()
    {
        var acme = OrgId.New();
        var rival = OrgId.New();
        await SeedCustomerAsync(acme, "Acme Refrigeration");
        await SeedCustomerAsync(rival, "Rival Heating");

        await _factory.SeedUserAsync(acme, "dispatcher@acme.example", "shift-plan-monday", UserRole.Dispatcher);
        using var client = _factory.CreateClient();
        var token = await client.LoginAsync("dispatcher@acme.example", "shift-plan-monday");

        using var response = await GetMyCustomersAsync(client, token);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var page = await response.Content.ReadFromJsonAsync<CustomerPageResponse>();
        var customers = page?.Items;
        Assert.NotNull(customers);
        Assert.Equal(["Acme Refrigeration"], customers.Select(customer => customer.Name));
    }

    [Fact]
    public async Task ARequestWithNoValidOrgClaimIsRejected()
    {
        using var client = _factory.CreateClient();
        var jwt = _factory.Services.GetRequiredService<IOptions<JwtOptions>>().Value;

        // Signed with the real key and carrying a real role, so this isolates exactly one thing:
        // a validated, correctly-authorized principal with no org claim at all. Authentication
        // and role authorization both have to succeed for the request to even reach
        // TenantResolutionMiddleware.
        var handler = new JwtSecurityTokenHandler();
        var token = handler.WriteToken(new JwtSecurityToken(
            jwt.Issuer,
            jwt.Audience,
            [new Claim(AuthClaimTypes.Role, nameof(UserRole.Admin))],
            expires: DateTime.UtcNow.AddHours(1),
            signingCredentials: new SigningCredentials(
                new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwt.SigningKey)),
                SecurityAlgorithms.HmacSha256)));

        using var response = await GetMyCustomersAsync(client, token);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    private static Task<HttpResponseMessage> GetMyCustomersAsync(HttpClient client, string token) =>
        client.SendAsync(new HttpRequestMessage(HttpMethod.Get, "/customers").Authorized(token));

    /// <summary>
    /// Writes a customer straight through EF rather than through <c>POST /customers</c> —
    /// <c>TenantIsolationTests</c> seeds the same way, for the same reason: this test is about
    /// whether a <em>read</em> is scoped, and a second org's worth of login/token ceremony just
    /// to write a row would test the write path twice over.
    /// </summary>
    private async Task SeedCustomerAsync(OrgId org, string name)
    {
        await using var context = _postgres.NewContext(org);

        context.Customers.Add(CustomerBuilder.Any().ForOrg(org).Named(name).Build());
        await context.SaveChangesAsync();
    }
}
