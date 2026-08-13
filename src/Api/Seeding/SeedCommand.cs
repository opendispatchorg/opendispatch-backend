using System.Globalization;
using OpenDispatch.Infrastructure.Seeding;

namespace OpenDispatch.Api.Seeding;

/// <summary>
/// <c>make seed</c>: the host built for its configuration and its container, run once, and exited
/// without ever listening.
/// </summary>
/// <remarks>
/// <para>
/// A verb on the Api rather than a console project of its own, because everything the seeder needs
/// is already composed here — the validated connection string, the persistence registration, the
/// clock, the user store — and a second composition root is a second chance to compose half a host
/// (the same argument <c>AddInfrastructure</c> makes for existing in the first place).
/// </para>
/// <para>
/// It exits before any middleware is wired and before <c>app.Run()</c>, so nothing binds a port and
/// no hosted service starts. Seeding is the whole process, not a side effect of serving.
/// </para>
/// </remarks>
internal static class SeedCommand
{
    /// <summary>The argument that asks for a seed rather than a server.</summary>
    private const string Verb = "seed";

    /// <summary>Whether this process was started to seed rather than to serve.</summary>
    /// <param name="args">The host's command-line arguments.</param>
    public static bool Requested(string[] args) => Array.Exists(args, argument =>
        string.Equals(argument, Verb, StringComparison.Ordinal));

    /// <summary>Seeds the demo dataset and reports what it loaded.</summary>
    /// <param name="app">The built host, for its services and its environment.</param>
    /// <returns>The process exit code: zero if it seeded, one if it refused.</returns>
    public static async Task<int> RunAsync(WebApplication app)
    {
        // Checked here as well as by the registration, so refusing says why. Without it a
        // production host would fail on GetRequiredService with a dependency-injection message
        // about a type nobody has heard of, which is a true statement of the wrong fact.
        if (!DemoSeeding.IsAllowedIn(app.Environment.EnvironmentName))
        {
            SeedLog.Refused(app.Logger, DemoSeeding.RequiredEnvironment, app.Environment.EnvironmentName);

            return 1;
        }

        await using var scope = app.Services.CreateAsyncScope();
        var seed = await scope.ServiceProvider.GetRequiredService<DemoSeeder>()
            .SeedAsync(CancellationToken.None)
            .ConfigureAwait(false);

        var day = seed.Day.Start.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);

        SeedLog.Seeded(
            app.Logger,
            seed.OrganizationName,
            day,
            seed.Technicians,
            seed.Customers,
            seed.Locations,
            seed.Jobs);

        foreach (var login in seed.Logins)
        {
            SeedLog.Login(app.Logger, login.Username, login.Password, login.Role);
        }

        SeedLog.NextStep(app.Logger, day);

        return 0;
    }
}
