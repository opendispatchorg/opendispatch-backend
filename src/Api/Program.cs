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

    // The REST half of the API contract (Document 2 §11). The same registration serves the
    // document at /openapi/v1.json for a running host and feeds the build-time export that
    // `make gen-contracts` turns into TypeScript.
    builder.Services.AddOpenApi(options => options.AddDocumentTransformer((document, _, _) =>
    {
        // The default title is the assembly name, which reads like an implementation detail
        // in a document three repositories generate their clients from.
        document.Info.Title = "OpenDispatch";
        document.Info.Description =
            "The REST half of the OpenDispatch API contract. SignalR board events and the "
            + "offline-sync payloads are not describable here; they live in @opendispatch/contracts.";

        return Task.CompletedTask;
    }));

    var app = builder.Build();

    app.UseSerilogRequestLogging();

    // Served for a developer poking at a running host. The document the clients are generated
    // from is the one exported at build time, so there is nothing to gain from enumerating the
    // API surface to anonymous callers in production.
    if (app.Environment.IsDevelopment())
    {
        app.MapOpenApi();
    }

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
