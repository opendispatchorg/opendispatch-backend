using System.Globalization;
using OpenDispatch.Api.Configuration;
using OpenDispatch.Api.Health;
using Serilog;

// Bootstrap logger, replaced by the configured pipeline once the host is built. It exists
// so failures *before* that point — configuration binding rejecting a missing connection
// string, for instance — are still written somewhere a human will see.
// Invariant culture keeps log output identical regardless of the host's locale.
Log.Logger = new LoggerConfiguration()
    .WriteTo.Console(formatProvider: CultureInfo.InvariantCulture)
    .CreateBootstrapLogger();

try
{
    var builder = WebApplication.CreateBuilder(args);

    builder.Host.UseSerilog((context, services, logger) => logger
        .ReadFrom.Configuration(context.Configuration)
        .ReadFrom.Services(services)
        .Enrich.FromLogContext());

    builder.Services.AddDatabaseOptions(builder.Configuration);

    var app = builder.Build();

    app.UseSerilogRequestLogging();
    app.MapHealthEndpoint();

    app.Run();
    return 0;
}
catch (Exception ex)
{
    Log.Fatal(ex, "OpenDispatch API terminated unexpectedly.");
    return 1;
}
finally
{
    Log.CloseAndFlush();
}

/// <summary>
/// Exposes the generated entry point so <c>Api.IntegrationTests</c> can boot the real host
/// through <c>WebApplicationFactory&lt;Program&gt;</c>.
/// </summary>
public partial class Program
{
}
