using System.Globalization;
using System.Text;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;
using OpenDispatch.Api.Auth;
using OpenDispatch.Api.Configuration;
using OpenDispatch.Api.ErrorHandling;
using OpenDispatch.Api.Health;
using OpenDispatch.Api.Tenancy;
using OpenDispatch.Application;
using OpenDispatch.Application.Auth;
using OpenDispatch.Infrastructure;
using OpenDispatch.Infrastructure.Auth;
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
    builder.Services.AddAttachmentOptions(builder.Configuration);
    builder.Services.AddJwtOptions(builder.Configuration);

    // The request pipeline every feature slice rides on: MediatR, the validators, and the
    // logging/validation/transaction behaviors in that order. The order lives with the
    // behaviors rather than here, so it cannot be half-stated in two places.
    builder.Services.AddApplication();

    // The one place Api reaches into Infrastructure: registration, at startup, with the
    // connection string it owns. Everything above this line stays ignorant of EF Core.
    builder.Services.AddInfrastructure(
        provider => provider.GetRequiredService<IOptions<DatabaseOptions>>().Value.ConnectionString,
        provider => provider.GetRequiredService<IOptions<AttachmentOptions>>().Value.Root,
        provider =>
        {
            var jwt = provider.GetRequiredService<IOptions<JwtOptions>>().Value;
            return new JwtSigningOptions(
                jwt.SigningKey,
                jwt.Issuer,
                jwt.Audience,
                TimeSpan.FromMinutes(jwt.ExpiryMinutes));
        });

    // JWT bearer auth (Document 2 §7): the token OpenDispatch.Infrastructure.Auth.JwtTokenIssuer
    // writes is what this validates. Configured through the DI-resolving overload rather than a
    // literal read of builder.Configuration, so there is exactly one bound, validated JwtOptions
    // in the whole host and the signing side and the validating side cannot read it differently.
    builder.Services
        .AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
        .AddJwtBearer();

    builder.Services
        .AddOptions<JwtBearerOptions>(JwtBearerDefaults.AuthenticationScheme)
        .Configure<IOptions<JwtOptions>>((bearerOptions, jwtOptions) =>
        {
            var jwt = jwtOptions.Value;

            // The handler's inbound claim map otherwise rewrites well-known short JWT claim
            // names to the long ClaimTypes.* URIs while validating — "role" becomes
            // ".../identity/claims/role" on the way in, silently orphaning the RoleClaimType
            // setting below (verified: without this, every role check fails and every caller
            // is a 403). Off, so a claim is read back exactly as JwtTokenIssuer wrote it.
            bearerOptions.MapInboundClaims = false;

            bearerOptions.TokenValidationParameters = new TokenValidationParameters
            {
                ValidIssuer = jwt.Issuer,
                ValidAudience = jwt.Audience,
                IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwt.SigningKey)),

                // Short custom claim names, not the ClaimTypes.* URIs — AuthClaimTypes.Role must
                // say the same thing JwtTokenIssuer wrote, or [Authorize(Roles=)] silently
                // authorizes nobody.
                RoleClaimType = AuthClaimTypes.Role,
                ClockSkew = TimeSpan.FromSeconds(30),
            };
        });

    // Three roles (Document 2 §7), one policy each. A later step's endpoint asks for
    // AuthPolicies.AdminOnly rather than [Authorize(Roles = "Admin")], so a typo fails to
    // compile instead of silently authorizing nobody.
    builder.Services.AddAuthorizationBuilder()
        .AddPolicy(AuthPolicies.AdminOnly, policy => policy.RequireRole(nameof(UserRole.Admin)))
        .AddPolicy(AuthPolicies.DispatcherOnly, policy => policy.RequireRole(nameof(UserRole.Dispatcher)))
        .AddPolicy(AuthPolicies.TechnicianOnly, policy => policy.RequireRole(nameof(UserRole.Technician)));

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

    // Consistent, typed error responses (Document 3, step 46): ProblemDetails formatting for
    // everything that produces one — the exception handler below, Results.Problem/
    // ValidationProblem in ResultHttpMapping, and the framework's own (malformed body, no
    // matching route) failures — from the one registration.
    builder.Services.AddProblemDetails();
    builder.Services.AddExceptionHandler<UnhandledExceptionHandler>();

    var app = builder.Build();

    app.UseSerilogRequestLogging();

    // After request logging, so a request that ends in an unhandled exception is still logged
    // as a request (with the 500 UnhandledExceptionHandler leaves behind) rather than logged
    // twice — once by the exception it caught, once by the response it wrote. See
    // UnhandledExceptionHandler's remarks for the ordering argument. Before everything else, so
    // it catches an exception from any of it — including auth and tenant resolution.
    app.UseExceptionHandler();

    // Authentication before authorization before tenant resolution, all three before any
    // endpoint: an anonymous or wrong-role caller is rejected before routing hands the request
    // to a handler, and a caller who passed both of those but carries no valid org claim
    // (Document 2 §8, step 45) never reaches one either — nothing below this point may read
    // tenant-scoped data without ITenantContext already knowing whose it is.
    app.UseAuthentication();
    app.UseAuthorization();
    app.UseTenantResolution();

    // Served for a developer poking at a running host. The document the clients are generated
    // from is the one exported at build time, so there is nothing to gain from enumerating the
    // API surface to anonymous callers in production.
    if (app.Environment.IsDevelopment())
    {
        app.MapOpenApi();
    }

    app.MapHealthEndpoint();
    app.MapAuthEndpoints();
    app.MapTenancyTestEndpoints();
    app.MapErrorMappingTestEndpoints();

    app.Run();
    return 0;
}
// HostAbortedException is not a failure: it is how the EF Core design-time tools stop the host
// once they have the service provider they came for. Catching it would turn every
// `dotnet ef` command into "terminated unexpectedly" and a non-zero exit.
catch (Exception ex) when (ex is not HostAbortedException)
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
