using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using OpenDispatch.Api.IntegrationTests.Fixtures;
using OpenDispatch.Application.Auth;
using OpenDispatch.Application.Customers;
using OpenDispatch.Domain.Identifiers;
using OpenDispatch.TestSupport;

namespace OpenDispatch.Api.IntegrationTests.ErrorHandling;

/// <summary>
/// The two shapes <c>/auth/login</c> does not already exercise for real — a <c>NotFound</c>
/// <c>Result</c>, and a bug — against the real host (Document 3, step 46).
/// </summary>
/// <remarks>
/// <para>
/// The validation and unauthorized categories have their own tests already, through the real
/// login endpoint (<c>AuthFlowTests</c>). <c>NotFound</c> now has one too — <c>GET
/// /customers/{id}</c>, step 47's, replacing the temporary endpoint this file used before real
/// endpoints existed to prove it. <c>Conflict</c> still has no test of its own here; step 47's
/// own flow tests are where it first gets one, through <c>AssignJob</c>/<c>ChangeJobStatus</c>.
/// </para>
/// <para>
/// That <c>NotFound</c> test reads through a repository, so this class points the host at the
/// shared container. It did not until step 52's follow-up — a missing row and an unreachable
/// database are both "no customer came back" to a test that only checks the status code, which is
/// what let this one pass for five steps against whatever the developer's own <c>docker
/// compose</c> happened to be serving. See <c>DECISIONS.local.md</c>.
/// </para>
/// </remarks>
[Collection(PostgresCollectionDefinition.Name)]
[Trait(TestCategories.Name, TestCategories.Integration)]
public sealed class ErrorMappingFlowTests : IClassFixture<ApiFactory>
{
    private readonly ApiFactory _factory;

    public ErrorMappingFlowTests(ApiFactory factory, PostgresFixture postgres)
    {
        _factory = factory;
        _factory.ConnectionString = postgres.ConnectionString;
    }

    [Fact]
    public async Task ANotFoundResultYieldsAProblemDetailsResponse()
    {
        await _factory.SeedUserAsync(OrgId.New(), "reader@vance.example", "shift-plan-monday", UserRole.Dispatcher);
        using var client = _factory.CreateClient();
        var token = await client.LoginAsync("reader@vance.example", "shift-plan-monday");

        using var request = new HttpRequestMessage(HttpMethod.Get, $"/customers/{Guid.NewGuid()}").Authorized(token);
        using var response = await client.SendAsync(request);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);

        var problem = await response.Content.ReadFromJsonAsync<ProblemDetails>();
        Assert.NotNull(problem);
        Assert.Equal(StatusCodes.Status404NotFound, problem.Status);
        Assert.Equal(CustomerErrors.NotFoundCode, problem.Extensions["code"]?.ToString());
        AssertCarriesATraceId(problem);
    }

    /// <summary>
    /// The endpoint under test throws with the kind of message a real bug would carry — a
    /// connection string — so this is the test that would fail if <c>UnhandledExceptionHandler</c>
    /// ever passed <see cref="Exception.Message"/> straight through.
    /// </summary>
    [Fact]
    public async Task AnUnhandledExceptionYields500WithoutLeakingInternals()
    {
        using var client = _factory.CreateClient();

        using var response = await client.GetAsync("/_diagnostics/throws");

        Assert.Equal(HttpStatusCode.InternalServerError, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);

        var body = await response.Content.ReadAsStringAsync();
        Assert.DoesNotContain("prod-db", body, StringComparison.Ordinal);
        Assert.DoesNotContain("hunter2", body, StringComparison.Ordinal);
        Assert.DoesNotContain(nameof(InvalidOperationException), body, StringComparison.Ordinal);

        var problem = JsonSerializer.Deserialize<ProblemDetails>(body, JsonSerializerOptions.Web);
        Assert.NotNull(problem);
        Assert.Equal(StatusCodes.Status500InternalServerError, problem.Status);
        AssertCarriesATraceId(problem);
    }

    /// <summary>
    /// A request the framework cannot bind at all is the caller's problem, not the server's, and
    /// answering 500 both blamed the wrong party and told a client to retry something that will
    /// fail identically forever.
    /// </summary>
    /// <remarks>
    /// Two shapes, one rule: a required query parameter that was not sent (found by hand while
    /// walking the step-53 demo — <c>GET /sync/pull</c> with no cursor answered 500) and a body
    /// that is not the JSON the endpoint declared, which was the same 500 on every POST in the
    /// API and had never been noticed.
    /// </remarks>
    [Theory]
    [InlineData("missing query parameter")]
    [InlineData("malformed body")]
    public async Task ARequestTheServerCannotReadIsABadRequestNotAServerError(string shape)
    {
        await _factory.SeedUserAsync(
            OrgId.New(), $"tech-{shape.Replace(' ', '-')}@vance.example", "boiler-service-call", UserRole.Technician,
            TechnicianId.New());
        using var client = _factory.CreateClient();
        var token = await client.LoginAsync($"tech-{shape.Replace(' ', '-')}@vance.example", "boiler-service-call");

        using var request = shape == "missing query parameter"
            ? new HttpRequestMessage(HttpMethod.Get, "/sync/pull").Authorized(token)
            : new HttpRequestMessage(HttpMethod.Post, "/sync/push")
            {
                Content = new StringContent("{ not json", Encoding.UTF8, "application/json"),
            }.Authorized(token);

        using var response = await client.SendAsync(request);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);

        var problem = await response.Content.ReadFromJsonAsync<ProblemDetails>();
        Assert.NotNull(problem);
        Assert.Equal(StatusCodes.Status400BadRequest, problem.Status);
        AssertCarriesATraceId(problem);
    }

    /// <summary>
    /// Both facts above go through a different path to a ProblemDetails response — one a
    /// <c>Result</c> mapped, one a raw exception the handler caught — so asserting this on both
    /// is what actually proves <c>CustomizeProblemDetails</c> (Program.cs) covers every path
    /// rather than merely the one it happened to be written against.
    /// </summary>
    private static void AssertCarriesATraceId(ProblemDetails problem)
    {
        Assert.True(problem.Extensions.TryGetValue("traceId", out var traceId), "no traceId extension.");
        Assert.False(string.IsNullOrWhiteSpace(traceId?.ToString()));
    }
}
