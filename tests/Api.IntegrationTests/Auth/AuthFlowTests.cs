using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Mvc;
using OpenDispatch.Api.IntegrationTests.Fixtures;
using OpenDispatch.Application.Auth;
using OpenDispatch.Contracts.Auth;
using OpenDispatch.Contracts.Technicians;
using OpenDispatch.Domain.Identifiers;
using OpenDispatch.TestSupport;

namespace OpenDispatch.Api.IntegrationTests.Auth;

/// <summary>
/// Login and role enforcement against the real host — the first requests in this suite that go
/// through <c>UseAuthentication</c>/<c>UseAuthorization</c>, which is why they have to be here
/// rather than sent through <c>ISender</c> directly like the pre-controller slices are: both are
/// ASP.NET Core middleware behaviour, invisible to anything that does not make a real HTTP
/// request against a real host.
/// </summary>
/// <remarks>
/// <para>
/// Login itself needs no database — the minimal user store is an in-memory singleton
/// (<c>InMemoryUserStore</c>) — but <see cref="AProtectedEndpointAcceptsTheRoleItAsksFor"/> does:
/// proving a policy <em>admits</em> the right role means letting the request through to a handler
/// that really writes a technician. So this class points the host at the shared container like
/// every other <see cref="ApiFactory"/> class does, rather than leaving
/// <see cref="ApiFactory.ConnectionString"/> unset.
/// </para>
/// <para>
/// It did leave it unset until step 52's follow-up, which is how these tests spent steps 47–52
/// quietly talking to whatever happened to be listening on the developer's own
/// <c>docker compose</c> port instead of to a container of their own — green on a machine with
/// that database running, a 500 on any that had none. See <c>DECISIONS.local.md</c>.
/// </para>
/// </remarks>
[Collection(PostgresCollectionDefinition.Name)]
[Trait(TestCategories.Name, TestCategories.Integration)]
public sealed class AuthFlowTests : IClassFixture<ApiFactory>
{
    private static readonly DateTimeOffset MorningOf = new(2026, 8, 10, 8, 0, 0, TimeSpan.Zero);

    private readonly ApiFactory _factory;

    public AuthFlowTests(ApiFactory factory, PostgresFixture postgres)
    {
        _factory = factory;
        _factory.ConnectionString = postgres.ConnectionString;
    }

    [Fact]
    public async Task LoginWithTheRightPasswordReturnsAToken()
    {
        await _factory.SeedUserAsync(OrgId.New(), "dana@vance.example", "correct horse battery", UserRole.Admin);
        using var client = _factory.CreateClient();

        using var response = await client.PostAsJsonAsync(
            "/auth/login",
            new LoginRequest("dana@vance.example", "correct horse battery"));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var body = await response.Content.ReadFromJsonAsync<LoginResponse>();

        Assert.False(string.IsNullOrWhiteSpace(body?.Token));
        Assert.True(body!.ExpiresAt > DateTimeOffset.UtcNow);
    }

    [Theory]
    [InlineData("dana@vance.example", "the wrong password")]
    [InlineData("nobody-by-this-name@vance.example", "correct horse battery")]
    public async Task LoginWithWrongCredentialsIsRejected(string username, string password)
    {
        await _factory.SeedUserAsync(OrgId.New(), "dana@vance.example", "correct horse battery", UserRole.Admin);
        using var client = _factory.CreateClient();

        using var response = await client.PostAsJsonAsync("/auth/login", new LoginRequest(username, password));

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    /// <summary>
    /// The validation category through the real endpoint, not a temporary one — step 46's
    /// mapping proven by the one handler that already fails this way for a real reason.
    /// </summary>
    [Fact]
    public async Task LoginWithAnEmptyUsernameYieldsAValidationProblem()
    {
        using var client = _factory.CreateClient();

        using var response = await client.PostAsJsonAsync("/auth/login", new LoginRequest(string.Empty, "anything"));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);

        var problem = await response.Content.ReadFromJsonAsync<ValidationProblemDetails>();
        Assert.NotNull(problem);
        Assert.Contains(nameof(LoginRequest.Username), problem.Errors.Keys);
    }

    /// <summary>
    /// <c>POST /technicians</c> is <c>AdminOnly</c> (step 47) — real, rather than a temporary
    /// endpoint built to prove the pipeline: steps 44/45 needed one because nothing role-guarded
    /// existed yet, and step 47 is exactly the step that stops that being true.
    /// </summary>
    [Fact]
    public async Task AProtectedEndpointRejectsAnAnonymousCaller()
    {
        using var client = _factory.CreateClient();

        using var response = await client.PostAsync("/technicians", content: null);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    /// <summary>
    /// Somebody who has left the shop cannot sign in, and cannot tell that they were switched off
    /// rather than mistyped: a disabled login answers exactly as a wrong password does.
    /// </summary>
    /// <remarks>
    /// The half of a user lifecycle this system has. What it does <em>not</em> do is take back the
    /// token they already hold — that outlives the change by up to <c>Jwt:ExpiryMinutes</c>, which
    /// is stated in the README rather than discovered during an incident.
    /// </remarks>
    [Fact]
    public async Task ADisabledLoginIsRefused()
    {
        var username = $"leaver-{Guid.NewGuid():N}@vance.example";
        await _factory.SeedUserAsync(OrgId.New(), username, "correct horse battery", UserRole.Dispatcher);

        using var client = _factory.CreateClient();

        // Signed in before, and not after: what changed is the login rather than the password.
        using (var before = await client.PostAsJsonAsync(
            "/auth/login", new LoginRequest(username, "correct horse battery")))
        {
            Assert.Equal(HttpStatusCode.OK, before.StatusCode);
        }

        await _factory.DisableUserAsync(username);

        using var after = await client.PostAsJsonAsync(
            "/auth/login", new LoginRequest(username, "correct horse battery"));

        Assert.Equal(HttpStatusCode.Unauthorized, after.StatusCode);
    }

    [Fact]
    public async Task AProtectedEndpointRejectsTheWrongRole()
    {
        await _factory.SeedUserAsync(OrgId.New(), "priya@vance.example", "shift-plan-monday", UserRole.Dispatcher);
        using var client = _factory.CreateClient();
        var token = await client.LoginAsync("priya@vance.example", "shift-plan-monday");

        using var request = new HttpRequestMessage(HttpMethod.Post, "/technicians").Authorized(token);
        using var response = await client.SendAsync(request);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task AProtectedEndpointAcceptsTheRoleItAsksFor()
    {
        await _factory.SeedUserAsync(OrgId.New(), "sam@vance.example", "boiler-service-call", UserRole.Admin);
        using var client = _factory.CreateClient();
        var token = await client.LoginAsync("sam@vance.example", "boiler-service-call");

        using var request = new HttpRequestMessage(HttpMethod.Post, "/technicians").Authorized(token);
        request.Content = JsonContent.Create(new CreateTechnicianRequest(
            "Alex Rivera", ["hvac"], MorningOf, MorningOf.AddHours(9), 51.5074d, -0.1278d));
        using var response = await client.SendAsync(request);

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
    }
}
