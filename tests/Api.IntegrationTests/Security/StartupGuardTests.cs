using System.Net;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using OpenDispatch.Api.Configuration;
using OpenDispatch.Api.IntegrationTests.Fixtures;
using OpenDispatch.TestSupport;

namespace OpenDispatch.Api.IntegrationTests.Security;

/// <summary>
/// What this host refuses to do outside the two environments this repository controls.
/// </summary>
/// <remarks>
/// <para>
/// The guards are worth testing precisely because they are invisible when they work: nothing about
/// a development run tells you whether a production one would have started with the published
/// signing key, and the day somebody finds out the hard way is the day it mattered.
/// </para>
/// <para>
/// <strong>Why the refusals are asserted against the options rather than against a booted
/// host.</strong> A host that fails validation is caught by <c>Program.cs</c>'s top-level handler,
/// which logs it as fatal and returns a non-zero exit code — correct for a real deployment, and it
/// means <c>WebApplicationFactory</c> only ever sees "the entry point exited", with the actual
/// verdict in a log line rather than in an exception it can be asked about. So the *reason* is
/// asserted where the guard lives, and the *consequence* — no host, nothing served — is asserted
/// beside it. Both halves, neither guessed.
/// </para>
/// </remarks>
[Collection(PostgresCollectionDefinition.Name)]
[Trait(TestCategories.Name, TestCategories.Integration)]
public sealed class StartupGuardTests
{
    private const string ARealSigningKey = "a-real-signing-key-for-this-test-host-only-32+";
    private const string ACommittedPasswordConnectionString =
        "Host=db.example;Database=opendispatch;Username=opendispatch;Password=opendispatch";

    /// <summary>
    /// The committed development signing key is long enough to satisfy the length rule, which is
    /// exactly why the length rule cannot be the whole guard: without this, a production host that
    /// forgot to override it starts happily and signs tokens anybody with the public repository can
    /// forge.
    /// </summary>
    [Fact]
    public void TheCommittedSigningKeyIsRefusedOutsideDevelopment()
    {
        var refused = Assert.Throws<OptionsValidationException>(
            () => JwtOptionsIn("Production", DevelopmentDefaults.JwtSigningKey));

        Assert.Contains("Jwt:SigningKey", string.Join(" ", refused.Failures), StringComparison.Ordinal);
    }

    /// <summary>
    /// A database still carrying the password this repository publishes in <c>appsettings.json</c>
    /// and <c>docker-compose.yml</c> is the other half of the same mistake, matched inside the
    /// connection string so that pointing the demo password at a real host is caught too.
    /// </summary>
    [Fact]
    public void TheCommittedDatabasePasswordIsRefusedOutsideDevelopment()
    {
        var refused = Assert.Throws<OptionsValidationException>(
            () => DatabaseOptionsIn("Production", ACommittedPasswordConnectionString));

        Assert.Contains("Database:ConnectionString", string.Join(" ", refused.Failures), StringComparison.Ordinal);
    }

    /// <summary>
    /// The guard refuses two specific published values, not "production" — a deployment that has
    /// configured itself never meets it. Development and Testing are unaffected, which is what
    /// keeps `make run` and this suite working with no configuration at all.
    /// </summary>
    [Theory]
    [InlineData("Development")]
    [InlineData("Testing")]
    public void TheCommittedValuesAreFineWhereTheyBelong(string environment)
    {
        Assert.Equal(DevelopmentDefaults.JwtSigningKey, JwtOptionsIn(environment, DevelopmentDefaults.JwtSigningKey).SigningKey);
        Assert.NotNull(DatabaseOptionsIn(environment, ACommittedPasswordConnectionString));
    }

    [Fact]
    public void AKeyOfItsOwnIsAcceptedAnywhere() =>
        Assert.Equal(ARealSigningKey, JwtOptionsIn("Production", ARealSigningKey).SigningKey);

    /// <summary>
    /// The consequence, end to end: a production host carrying the committed key does not come up
    /// at all. Asserted as "no host" rather than by exception type, per this class's remarks.
    /// </summary>
    [Fact]
    public async Task AProductionHostCarryingThemDoesNotStart()
    {
        await using var factory = new ApiFactory { Environment = "Production" };

        Assert.ThrowsAny<Exception>(() => factory.CreateClient());
    }

    /// <summary>
    /// And the same host with a key of its own serves normally — proving the previous fact is the
    /// guard firing rather than "Production does not work here".
    /// </summary>
    [Fact]
    public async Task AProductionHostWithItsOwnKeyStarts()
    {
        await using var factory = ProductionHost();
        using var client = factory.CreateClient();

        using var response = await client.GetAsync("/health/live");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    /// <summary>
    /// The fixture that exists to throw is not a route a public host serves. It answers 404 outside
    /// Development/Testing because it is never mapped there — the guard is the mapping, not a check
    /// inside the handler.
    /// </summary>
    [Fact]
    public async Task TheDiagnosticsFixtureIsNotThereOutsideDevelopment()
    {
        await using var factory = ProductionHost();
        using var client = factory.CreateClient();

        using var response = await client.GetAsync("/_diagnostics/throws");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    private static ApiFactory ProductionHost() =>
        new()
        {
            Environment = "Production",
            Settings = { ["Jwt:SigningKey"] = ARealSigningKey },
        };

    /// <summary>Resolves the real registration's options, which is where the guard runs.</summary>
    private static JwtOptions JwtOptionsIn(string environment, string signingKey) =>
        Resolve<JwtOptions>(
            services => services.AddJwtOptions(
                Configuration(new Dictionary<string, string?>
                {
                    ["Jwt:SigningKey"] = signingKey,
                    ["Jwt:Issuer"] = "opendispatch",
                    ["Jwt:Audience"] = "opendispatch-clients",
                    ["Jwt:ExpiryMinutes"] = "60",
                }),
                new Environment(environment)));

    private static DatabaseOptions DatabaseOptionsIn(string environment, string connectionString) =>
        Resolve<DatabaseOptions>(
            services => services.AddDatabaseOptions(
                Configuration(new Dictionary<string, string?>
                {
                    ["Database:ConnectionString"] = connectionString,
                }),
                new Environment(environment)));

    private static TOptions Resolve<TOptions>(Action<IServiceCollection> register)
        where TOptions : class
    {
        var services = new ServiceCollection();
        register(services);

        using var provider = services.BuildServiceProvider();

        return provider.GetRequiredService<IOptions<TOptions>>().Value;
    }

    private static IConfiguration Configuration(Dictionary<string, string?> settings) =>
        new ConfigurationBuilder().AddInMemoryCollection(settings).Build();

    /// <summary>The one thing the guards ask about, and nothing else a host environment carries.</summary>
    private sealed class Environment(string environmentName) : IHostEnvironment
    {
        public string EnvironmentName { get; set; } = environmentName;

        public string ApplicationName { get; set; } = "OpenDispatch.Api";

        public string ContentRootPath { get; set; } = AppContext.BaseDirectory;

        public IFileProvider ContentRootFileProvider { get; set; } =
            new NullFileProvider();
    }
}
