using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using OpenDispatch.Api.IntegrationTests.Fixtures;
using OpenDispatch.TestSupport;

namespace OpenDispatch.Api.IntegrationTests.ErrorHandling;

/// <summary>
/// The two shapes <c>/auth/login</c> does not already exercise for real — a <c>NotFound</c>
/// <c>Result</c>, and a bug — against the real host (Document 3, step 46).
/// </summary>
/// <remarks>
/// The validation and unauthorized categories have their own tests already, through the real
/// login endpoint (<c>AuthFlowTests</c>) — repeating them here through a temporary endpoint would
/// be the same assertion twice, once for real and once for a fixture. What only this file proves
/// is the shared mapping and the exception handler themselves, over the two categories nothing
/// real produces yet.
/// </remarks>
[Collection(PostgresCollectionDefinition.Name)]
[Trait(TestCategories.Name, TestCategories.Integration)]
public sealed class ErrorMappingFlowTests : IClassFixture<ApiFactory>
{
    private readonly ApiFactory _factory;

    public ErrorMappingFlowTests(ApiFactory factory) => _factory = factory;

    [Fact]
    public async Task ANotFoundResultYieldsAProblemDetailsResponse()
    {
        using var client = _factory.CreateClient();

        using var response = await client.GetAsync("/_test/error-mapping/not-found");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);

        var problem = await response.Content.ReadFromJsonAsync<ProblemDetails>();
        Assert.NotNull(problem);
        Assert.Equal(StatusCodes.Status404NotFound, problem.Status);
        Assert.Equal("test.notFound", problem.Extensions["code"]?.ToString());
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

        using var response = await client.GetAsync("/_test/error-mapping/throws");

        Assert.Equal(HttpStatusCode.InternalServerError, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);

        var body = await response.Content.ReadAsStringAsync();
        Assert.DoesNotContain("prod-db", body, StringComparison.Ordinal);
        Assert.DoesNotContain("hunter2", body, StringComparison.Ordinal);
        Assert.DoesNotContain(nameof(InvalidOperationException), body, StringComparison.Ordinal);

        var problem = JsonSerializer.Deserialize<ProblemDetails>(body, JsonSerializerOptions.Web);
        Assert.NotNull(problem);
        Assert.Equal(StatusCodes.Status500InternalServerError, problem.Status);
    }
}
