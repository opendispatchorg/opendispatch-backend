using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;

namespace OpenDispatch.Api.IntegrationTests.Fixtures;

/// <summary>
/// Boots the real Api host in-process. Endpoint tests go through this rather than
/// <see cref="WebApplicationFactory{TEntryPoint}"/> directly, so the whole suite picks up
/// host-level test configuration from one place.
/// </summary>
/// <remarks>
/// Helpers to seed an organization and users, issue a JWT and set the tenant belong here
/// too — they are added alongside the features they exercise, since there is no auth,
/// tenant context or Organization aggregate to seed yet.
/// </remarks>
public sealed class ApiFactory : WebApplicationFactory<Program>
{
    /// <summary>
    /// Points the host at a database, typically the shared <see cref="PostgresFixture"/>
    /// container. Leave null for endpoints that never touch one.
    /// </summary>
    /// <remarks>
    /// Set this before the first <c>CreateClient()</c> call: that is what builds the host,
    /// and configuration is read once during the build. A settable property rather than a
    /// constructor parameter because xunit requires a class fixture to expose exactly one
    /// public constructor.
    /// </remarks>
    public string? ConnectionString { get; set; }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Testing");

        if (ConnectionString is null)
        {
            return;
        }

        builder.ConfigureAppConfiguration((_, configuration) =>
            configuration.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Database:ConnectionString"] = ConnectionString,
            }));
    }
}
