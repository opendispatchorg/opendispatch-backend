using System.Globalization;
using System.Text;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;
using OpenDispatch.Api.Attachments;
using OpenDispatch.Api.Auth;
using OpenDispatch.Api.Board;
using OpenDispatch.Api.Configuration;
using OpenDispatch.Api.Customers;
using OpenDispatch.Api.Dispatch;
using OpenDispatch.Api.ErrorHandling;
using OpenDispatch.Api.Export;
using OpenDispatch.Api.Health;
using OpenDispatch.Api.Invoicing;
using OpenDispatch.Api.Jobs;
using OpenDispatch.Api.Observability;
using OpenDispatch.Api.OpenApi;
using OpenDispatch.Api.Operations;
using OpenDispatch.Api.Schedule;
using OpenDispatch.Api.Security;
using OpenDispatch.Api.Seeding;
using OpenDispatch.Api.Sync;
using OpenDispatch.Api.Technicians;
using OpenDispatch.Api.Tenancy;
using OpenDispatch.Api.Users;
using OpenDispatch.Application;
using OpenDispatch.Application.Abstractions;
using OpenDispatch.Application.Auth;
using OpenDispatch.Infrastructure;
using OpenDispatch.Infrastructure.Auth;
using OpenDispatch.Infrastructure.Events;
using OpenDispatch.Infrastructure.Notifications;
using OpenDispatch.Infrastructure.Seeding;
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

    // The console sink is chosen here rather than in Serilog:WriteTo, so `Logging:Json` is one
    // boolean instead of an assembly-qualified formatter name typed into JSON. Everything else about
    // the logger — levels, overrides, enrichers, any additional sink — still comes from
    // configuration. See ConsoleLogging.
    builder.Host.UseSerilog((context, services, logger) => logger
        .ReadFrom.Configuration(context.Configuration)
        .ReadFrom.Services(services)
        .Enrich.FromLogContext()
        .WriteToConsole(context.Configuration));

    // The environment is passed in because two of these validate against it: the signing key and
    // the database password this repository commits are development conveniences, and a host
    // outside Development/Testing that still carries them refuses to start rather than serving
    // traffic with published credentials. See DevelopmentDefaults.
    builder.Services.AddDatabaseOptions(builder.Configuration, builder.Environment);
    builder.Services.AddAttachmentOptions(builder.Configuration);
    builder.Services.AddSyncOptions(builder.Configuration);
    builder.Services.AddOutboxOptions(builder.Configuration);
    builder.Services.AddJwtOptions(builder.Configuration, builder.Environment);
    builder.Services.AddMailOptions(builder.Configuration);

    // The request pipeline every feature slice rides on: MediatR, the validators, and the
    // logging/validation/transaction behaviors in that order. The order lives with the
    // behaviors rather than here, so it cannot be half-stated in two places.
    builder.Services.AddApplication();

    // The one place Api reaches into Infrastructure: registration, at startup, with the
    // connection string it owns. Everything above this line stays ignorant of EF Core.
    builder.Services.AddInfrastructure(
        provider => provider.GetRequiredService<IOptions<DatabaseOptions>>().Value.ConnectionString,
        provider => provider.GetRequiredService<IOptions<AttachmentOptions>>().Value.ToStorageSettings(),
        provider =>
        {
            var jwt = provider.GetRequiredService<IOptions<JwtOptions>>().Value;
            return new JwtSigningOptions(
                jwt.SigningKey,
                jwt.Issuer,
                jwt.Audience,
                TimeSpan.FromMinutes(jwt.ExpiryMinutes));
        },

        // The outbox sweep's own settings. Read here rather than lazily for the reason the
        // backplane is: a hosted service's schedule is a structural choice about the container.
        // Bound and validated above, so a host with a nonsense interval fails at startup.
        Outbox(builder.Configuration));

    // The dev-only demo dataset (Document 3, step 53). Registers nothing outside Development, so
    // the seeder is not merely refused on a production host — it is not there. The hosted service
    // beside it establishes the demo logins this process will serve; see its own remarks for why
    // that cannot be `make seed`'s job.
    builder.Services.AddDemoSeeding(builder.Environment.EnvironmentName);

    if (DemoSeeding.IsAllowedIn(builder.Environment.EnvironmentName))
    {
        builder.Services.AddHostedService<DemoLoginRegistrar>();
    }

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

            // SignalR's browser transports (Server-Sent Events, long polling) cannot set an
            // Authorization header on the connection they negotiate with, so the JWT bearer
            // handler never sees the token unless something puts it somewhere a WebSocket/SSE
            // request can carry it — the query string, by SignalR's own convention. Scoped to
            // /hubs so an ordinary REST caller cannot bypass header-based auth by moving a token
            // into a URL, which is otherwise a weaker place for one to travel (logged by proxies,
            // kept in browser history).
            bearerOptions.Events = new JwtBearerEvents
            {
                OnMessageReceived = context =>
                {
                    var accessToken = context.Request.Query["access_token"];

                    if (accessToken.Count > 0 && context.HttpContext.Request.Path.StartsWithSegments("/hubs"))
                    {
                        context.Token = accessToken;
                    }

                    return Task.CompletedTask;
                },
            };
        });

    // Three roles (Document 2 §7), one policy each, plus the office-side pair step 47's
    // Customers/Technicians/Jobs endpoints use by default. A later step's endpoint asks for
    // AuthPolicies.AdminOnly rather than [Authorize(Roles = "Admin")], so a typo fails to
    // compile instead of silently authorizing nobody.
    // Every route says for itself who may call it, and the ones that anybody may call say that
    // too (AllowAnonymous on login, the three health probes, and the development-only diagnostics
    // and OpenAPI routes). A fallback policy would say it once here instead — and was tried — but
    // ASP.NET Core applies a fallback to requests that match *no* endpoint as well, which turns
    // every mistyped URL in the API into a 401 and contradicts step 46's answer for an unknown
    // path. What holds the rule instead is EndpointAuthorizationTests, which walks the host's own
    // endpoint list and fails if a route carries neither.
    builder.Services.AddAuthorizationBuilder()
        .AddPolicy(AuthPolicies.AdminOnly, policy => policy.RequireRole(nameof(UserRole.Admin)))
        .AddPolicy(AuthPolicies.DispatcherOnly, policy => policy.RequireRole(nameof(UserRole.Dispatcher)))
        .AddPolicy(AuthPolicies.TechnicianOnly, policy => policy.RequireRole(nameof(UserRole.Technician)))
        .AddPolicy(
            AuthPolicies.AdminOrDispatcher,
            policy => policy.RequireRole(nameof(UserRole.Admin), nameof(UserRole.Dispatcher)))
        .AddPolicy(
            AuthPolicies.AnyRole,
            policy => policy.RequireRole(
                nameof(UserRole.Admin),
                nameof(UserRole.Dispatcher),
                nameof(UserRole.Technician)));

    // The REST half of the API contract (Document 2 §11). The same registration serves the
    // document at /openapi/v1.json for a running host and feeds the build-time export that
    // `make gen-contracts` turns into TypeScript.
    builder.Services.AddOpenApi(options =>
    {
        options.AddDocumentTransformer((document, _, _) =>
        {
            // The default title is the assembly name, which reads like an implementation detail
            // in a document three repositories generate their clients from.
            document.Info.Title = "OpenDispatch";
            document.Info.Description =
                "The REST half of the OpenDispatch API contract. SignalR board events and the "
                + "offline-sync payloads are not describable here; they live in @opendispatch/contracts.";

            return Task.CompletedTask;
        });

        // Document 3, step 52: every route needs a bearer token except the two a client can
        // reach without one already (login, health) — see OpenApiSecurity's own remarks.
        options.AddBearerSecurityScheme();

        // Document 3, step 52: every route can fail, and every failure is shaped the same way
        // — see OpenApiErrorResponses' own remarks for why this is one document transformer
        // rather than per-route metadata.
        options.AddDefaultErrorResponse();
    });

    // Consistent, typed error responses (Document 3, step 46): ProblemDetails formatting for
    // everything that produces one — the exception handler below, Results.Problem/
    // ValidationProblem in ResultHttpMapping, and the framework's own (malformed body, no
    // matching route) failures — from the one registration.
    //
    // CustomizeProblemDetails is the one hook every one of those paths already funnels through,
    // so a traceId on every failure response (step 46's own "no owner" gap, closed here) costs
    // one line rather than one per call site. On its own this is half a fix: see
    // UseSerilogRequestLogging below for the other half — the same id landing somewhere a human
    // can actually grep for it.
    //
    // The value is step 54's correlation id: UseCorrelationId sets TraceIdentifier, so a caller
    // that sent X-Correlation-ID reads its own id back out of the failure body. The member keeps
    // the name "traceId" — it is what ASP.NET Core itself puts on a ProblemDetails and what a
    // client library expects to find — while the header and the log lines say "correlation". One
    // value, and the value is what anybody actually greps for.
    builder.Services.AddProblemDetails(options => options.CustomizeProblemDetails = context =>
        context.ProblemDetails.Extensions["traceId"] = context.HttpContext.TraceIdentifier);
    builder.Services.AddExceptionHandler<UnhandledExceptionHandler>();

    // A request whose parameters cannot be bound is routed through the same handler as everything
    // else, so it gets the same ProblemDetails body as every other failure.
    //
    // This flag is otherwise on in Development and off everywhere else, which is the worst of both:
    // on a development host a missing query parameter threw and became a 500 (found by hand while
    // walking the step-53 demo: GET /sync/pull with no cursor), and on any other host it answered a
    // bare 400 with an empty body and no correlation id. Set explicitly, both become a 400 that says
    // which parameter — UnhandledExceptionHandler's BadHttpRequestException branch is what turns it
    // into a body.
    builder.Services.Configure<RouteHandlerOptions>(options => options.ThrowOnBadRequest = true);

    // The real-time dispatch board (Document 2 §9, step 51), with a Redis backplane when one is
    // configured — without it this host's board is correct only while there is one of it, because
    // SignalR groups live in the memory of the process holding the connection. See BoardBackplane.
    //
    // The notifier is scoped, not singleton: it reads ITenantContext, which is itself scoped to the
    // request that raised the domain event it is answering.
    var backplane = builder.Services.AddDispatchBoardRealtime(builder.Configuration);
    builder.Services.AddScoped<IBoardNotifier, SignalRBoardNotifier>();

    // Metrics and traces to a collector, if this deployment named one. Registration-time like the
    // backplane, and for the same reason: which exporter a process holds is structural. See
    // Telemetry for why it is OTLP and why a dead collector cannot take the API with it.
    var telemetry = builder.Services.AddOpenDispatchTelemetry(builder.Configuration);

    // The two customer-facing messages this system sends, if this deployment named a mail server.
    // Registration-time like the backplane and the exporter, and for the same reason: whether a
    // process can reach the outside world is structural. Nothing is registered when nothing is
    // configured, so CustomerNotifications' own "no sender" branch is the truth rather than a no-op
    // reporting success for messages nobody sent. See MailOptions.
    var mail = builder.Services.AddNotifications(
        MailOptions.IsConfiguredIn(builder.Configuration),
        provider => provider.GetRequiredService<IOptions<MailOptions>>().Value.ToMailSettings());

    // The three edge concerns a host that faces something other than curl needs, each doing
    // nothing at all unless configured: a cap on credential guessing, the browser origins the two
    // client repositories are served from, and whether this host is behind a proxy whose forwarded
    // headers it should believe. See each type's own remarks.
    builder.Services.AddLoginRateLimiting(builder.Configuration);
    builder.Services.AddClientCors();
    builder.Services.AddReverseProxyForwarding();

    // A ceiling on how long a request may run and how large a body may be. Neither had one: a
    // request could run until the caller gave up while holding a database connection, and Kestrel's
    // default body limit sat above the attachment cap, so oversized uploads were read in full and
    // then refused. See RequestLimits.
    builder.Services.AddRequestLimits();

    var app = builder.Build();

    // `make seed` (Document 3, step 53). The host exists at this point but serves nothing: the
    // seeder wants the configuration and the container, not a listener, so this returns before any
    // middleware is wired and before app.Run() would start a hosted service or bind a port.
    if (SeedCommand.Requested(args))
    {
        return await SeedCommand.RunAsync(app, args).ConfigureAwait(false);
    }

    // The two verbs a real deployment runs, in the same shape and for the same reason: applying the
    // schema and writing the first login are things this host does once, from a terminal or a
    // deployment job, rather than things it does while serving. Unlike the seeder neither is
    // restricted to Development — a production database is exactly what they are for.
    if (MigrateCommand.Requested(args))
    {
        return await MigrateCommand.RunAsync(app).ConfigureAwait(false);
    }

    if (CreateUserCommand.Requested(args))
    {
        return await CreateUserCommand.RunAsync(app, args).ConfigureAwait(false);
    }

    // Its counterpart, and the reason it exists: somebody leaves the shop. See DisableUserCommand
    // for what it does not do — the token they already hold.
    if (DisableUserCommand.Requested(args))
    {
        return await DisableUserCommand.RunAsync(app, args).ConfigureAwait(false);
    }

    // Housekeeping, for a scheduler rather than a person: the sync op log and the removal notes are
    // the only tables nothing else ever deletes from. See PruneCommand for what that costs a device
    // that has been away longer than the window.
    if (PruneCommand.Requested(args))
    {
        return await PruneCommand.RunAsync(app, args).ConfigureAwait(false);
    }

    // Ahead of everything, including the correlation id and the request log: until the forwarded
    // headers are applied, every request below claims to come from the proxy over plain HTTP, so a
    // log line, a rate-limit partition and a scheme check would all be answering about the wrong
    // caller. Reads no header at all unless this host says it is behind a proxy.
    app.UseForwardedHeaders();

    // First in the pipeline (Document 3, step 54): everything below it — the request-summary line,
    // the exception handler, every handler log line, and the traceId on a ProblemDetails body —
    // reports the one id this establishes, and a caller that sent its own gets that one back.
    app.UseCorrelationId();

    // Beside the correlation id, and above everything that can write a response — including the
    // exception handler, so a 500 carries them as surely as a 200 does. Until now the only security
    // header anywhere in this host was the nosniff on attachment downloads.
    app.UseSecurityHeaders();

    // NotFound/Conflict/Unauthorized/Validation log nothing of their own beyond this one
    // request-summary line, so it is what has to carry the id AddProblemDetails above puts on a
    // failure response, or that id has nothing server-side to correlate against.
    //
    // EnrichDiagnosticContext alone is not enough: it attaches the property to the LogEvent, but
    // the default Console sink (appsettings.json's only configured sink, unconfigured further)
    // only renders a message template's own tokens — verified by actually running the host and
    // reading real console output, which showed the enrichment taking effect on the LogEvent but
    // nothing extra in the printed line. MessageTemplate is what the line itself is built from, so
    // the id has to be named there too, the same fix already applied to
    // UnhandledExceptionHandlerLog's own template just below.
    //
    // It reads TraceIdentifier rather than the log context UseCorrelationId also pushes, so this
    // line keeps working whatever a future reordering does to the middleware above it.
    app.UseSerilogRequestLogging(options =>
    {
        options.MessageTemplate =
            "HTTP {RequestMethod} {RequestPath} responded {StatusCode} in {Elapsed:0.0000} ms "
            + "(correlation {CorrelationId})";
        options.EnrichDiagnosticContext = (diagnosticContext, httpContext) =>
            diagnosticContext.Set(CorrelationIdMiddleware.LogPropertyName, httpContext.TraceIdentifier);
    });

    // After request logging, so a request that ends in an unhandled exception is still logged
    // as a request (with the 500 UnhandledExceptionHandler leaves behind) rather than logged
    // twice — once by the exception it caught, once by the response it wrote. See
    // UnhandledExceptionHandler's remarks for the ordering argument. Before everything else, so
    // it catches an exception from any of it — including auth and tenant resolution.
    app.UseExceptionHandler();

    // Before authentication, because a browser's preflight carries no token and must still be
    // answered, and before the rate limiter, because an OPTIONS a browser sends on its own is not
    // a sign-in attempt. With no origins configured the policy matches nothing and writes no
    // header, which is the same answer a host without CORS gives.
    app.UseCors(ClientCors.PolicyName);

    // Ahead of the endpoints it protects, and after CORS: a refused caller is turned away before
    // any handler, and before a password is verified at 100,000 iterations. Only routes that ask
    // for a policy are limited; everything else passes through untouched.
    app.UseRateLimiter();

    // After the limiter, so a refused caller costs nothing, and before routing hands the request to
    // a handler, which is what the timeout has to be able to cancel. The default policy applies to
    // every endpoint that does not opt out — see RequestLimits, and the hub below, which does.
    app.UseRequestTimeouts();

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
        // Anonymous like the rest of the development-only surface, and explicitly so now that the
        // host denies by default: a developer pointing a browser at the document has no token.
        app.MapOpenApi().AllowAnonymous();
    }

    app.MapHealthEndpoint();
    app.MapAuthEndpoints();
    app.MapDiagnosticsEndpoints(app.Environment);
    app.MapCustomerEndpoints();
    app.MapTechnicianEndpoints();
    app.MapJobEndpoints();
    app.MapScheduleEndpoints();
    app.MapDispatchEndpoints();
    app.MapInvoiceEndpoints();
    app.MapExportEndpoints();
    app.MapSyncEndpoints();
    app.MapAttachmentEndpoints();
    // No request timeout on the hub, and this is not an oversight: SignalR's fallback transports
    // are long polling and server-sent events, both of which hold a request open on purpose — for
    // ninety seconds at a time under the default keep-alive. The default policy would cut every one
    // of them at thirty seconds and break the live board on exactly the transports a browser falls
    // back to when WebSockets are unavailable, which is the case this host is most likely to meet
    // behind somebody's corporate proxy.
    app.MapHub<DispatchHub>("/hubs/dispatch").DisableRequestTimeout();

    // Said once, at startup, because it is the one property of this host that cannot be discovered
    // by looking at it: a board with no backplane works perfectly until a second instance exists.
    StartupLog.BoardRealtime(app.Logger, backplane);

    // The same reasoning, twice more: where this host's measurements go, and what shape its log
    // lines are, are both invisible from outside and both things an operator gets wrong silently.
    StartupLog.Telemetry(app.Logger, telemetry, app.Configuration[Telemetry.EndpointKey]);
    StartupLog.LogFormat(app.Logger, ConsoleLogging.IsJson(app.Configuration));

    // And the fourth, which is the one that loses data rather than merely behaving oddly: a host on
    // a platform with an ephemeral filesystem, storing photographs on that filesystem, works
    // perfectly until it is deployed again. See AttachmentOptions.
    StartupLog.AttachmentStore(app.Logger, app.Services.GetRequiredService<IOptions<AttachmentOptions>>().Value);

    // And the fifth: whether anybody outside the shop is told anything at all.
    StartupLog.Notifications(app.Logger, mail, app.Configuration[$"{MailOptions.SectionName}:Host"]);

    app.Run();
    return 0;

    // The outbox sweep's settings, read from the configuration this host has at registration time.
    // A local function rather than a statement, so it sits with its one caller.
    static OutboxOptions Outbox(IConfiguration configuration)
    {
        var settings = configuration.GetSection(OutboxDeliveryOptions.SectionName).Get<OutboxDeliveryOptions>()
            ?? new OutboxDeliveryOptions();

        return new OutboxOptions(
            settings.Enabled,
            TimeSpan.FromSeconds(settings.IntervalSeconds),
            TimeSpan.FromSeconds(settings.GraceSeconds),
            settings.BatchSize);
    }
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
