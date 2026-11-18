using System.Text.Encodings.Web;
using System.Text.Json.Serialization;
using System.Text.Unicode;
using System.Threading.RateLimiting;
using Emanet.Api.Common;
using Emanet.Api.Features.Auth;
using Emanet.Api.Features.Bookings;
using Emanet.Api.Features.Demo;
using Emanet.Api.Features.Listings;
using Emanet.Api.Logging;
using Emanet.Infrastructure;
using Emanet.Infrastructure.Persistence;
using Emanet.Infrastructure.Storage;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Scalar.AspNetCore;
using Serilog;
using Serilog.Core;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddSingleton<ILogEventEnricher, SensitiveDataEnricher>();
builder.Services.AddSerilog((services, logger) => logger
    .ReadFrom.Configuration(builder.Configuration)
    .ReadFrom.Services(services)
    .Enrich.FromLogContext());

var connectionString = ConnectionStrings.FromUrl(builder.Configuration.GetConnectionString("Emanet"));
if (string.IsNullOrWhiteSpace(connectionString))
{
    throw new InvalidOperationException(
        "ConnectionStrings:Emanet is not configured. Use 'dotnet user-secrets' or the ConnectionStrings__Emanet environment variable.");
}

builder.Services.AddSingleton(TimeProvider.System);
builder.Services.AddEmanetPersistence(connectionString);
builder.Services.AddEmanetAuth(builder.Configuration);

builder.Services.AddEmanetStorage(builder.Configuration, connectionString);
builder.Services.AddScoped<BookingService>();
builder.Services.Configure<SweeperOptions>(builder.Configuration.GetSection(SweeperOptions.Section));
builder.Services.AddSingleton<BookingSweeper>();
if (builder.Configuration.GetValue($"{SweeperOptions.Section}:Enabled", true))
{
    builder.Services.AddHostedService<SweeperWorker>();
}

// The public demo's neighbourhood; off unless Demo:Enabled.
builder.Services.Configure<DemoOptions>(builder.Configuration.GetSection(DemoOptions.Section));
var demo = builder.Configuration.GetSection(DemoOptions.Section).Get<DemoOptions>() ?? new DemoOptions();
if (demo.Enabled)
{
    builder.Services.AddHttpClient(DemoWorker.HttpClientName, client => client.BaseAddress = new Uri(demo.BaseUrl));
    builder.Services.AddHostedService<DemoWorker>();
}

builder.Services.ConfigureHttpJsonOptions(options =>
{
    options.SerializerOptions.Encoder = JavaScriptEncoder.Create(UnicodeRanges.All);
    options.SerializerOptions.NumberHandling = JsonNumberHandling.Strict;
    options.SerializerOptions.Converters.Add(new JsonStringEnumConverter());
});

builder.Services.AddRateLimiter(options =>
{
    options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
    // Sign-up and sign-in per address: plenty for a person, slow for guessing.
    var perMinute = builder.Configuration.GetValue("Auth:AttemptsPerMinute", 10);
    // The demo's own people sign in from inside the container and are not held to it.
    options.AddPolicy(AuthEndpoints.RateLimit, context => demo.Enabled && context.Connection.RemoteIpAddress is { } address && System.Net.IPAddress.IsLoopback(address)
        ? RateLimitPartition.GetNoLimiter("demo")
        : RateLimitPartition.GetFixedWindowLimiter(
            context.Connection.RemoteIpAddress?.ToString() ?? "unknown",
            _ => new FixedWindowRateLimiterOptions { PermitLimit = perMinute, Window = TimeSpan.FromMinutes(1), QueueLimit = 0 }));
    options.OnRejected = async (context, _) =>
    {
        context.HttpContext.Response.Headers.RetryAfter = "60";
        await TypedResults.Problem(
            title: "Too many requests",
            detail: "Slow down and try again in a minute.",
            statusCode: StatusCodes.Status429TooManyRequests,
            extensions: new Dictionary<string, object?> { ["code"] = Emanet.Core.ErrorCodes.RateLimited })
            .ExecuteAsync(context.HttpContext);
    };
});

builder.Services.AddProblemDetails(options => options.CustomizeProblemDetails = context =>
    context.ProblemDetails.Extensions["traceId"] = context.HttpContext.TraceIdentifier);
builder.Services.AddExceptionHandler<DomainExceptionHandler>();

builder.Services.AddOpenApi(options => options
    .AddDocumentTransformer<DocumentInfoTransformer>()
    .AddDocumentTransformer<SecuritySchemesTransformer>()
    .AddOperationTransformer<SecuritySchemesTransformer>());
builder.Services.AddHealthChecks()
    .AddDbContextCheck<EmanetDbContext>("database", tags: ["ready"]);

var app = builder.Build();

// One container for the public demo: the built web app is copied into wwwroot and served from here, with the same
// headers nginx sets in Compose. Without a wwwroot (tests, Compose), nothing changes.
var indexPage = Path.Combine(app.Environment.ContentRootPath, "wwwroot", "index.html");
var serveWeb = File.Exists(indexPage);
app.UseMiddleware<CorrelationIdMiddleware>();
if (serveWeb)
{
    app.UseMiddleware<WebHeadersMiddleware>();
    app.UseDefaultFiles();
    app.UseStaticFiles(new StaticFileOptions
    {
        OnPrepareResponse = context => context.Context.Response.Headers.CacheControl =
            context.Context.Request.Path.StartsWithSegments("/assets") ? "public, max-age=31536000, immutable" : "no-cache",
    });
}

app.UseSerilogRequestLogging();
app.UseExceptionHandler();
app.UseStatusCodePages();
app.UseAuthentication();
app.UseAuthorization();
app.UseRateLimiter();

app.MapOpenApi();
app.MapScalarApiReference(options => options
    .WithTitle("Emanet")
    .WithTheme(ScalarTheme.Default)
    .ForceLightMode()
    .HideDarkModeToggle()
    .DisableDefaultFonts()
    .WithCustomCss(ScalarStyle.Css)
    .AddPreferredSecuritySchemes(SecuritySchemesTransformer.Scheme)
    .HideDeveloperTools()
    .HideClientButton()
    .DisableAgent()
    .DisableMcp()
    .DisableTelemetry());

if (!serveWeb)
{
    app.MapGet("/", () => Results.Redirect("/scalar")).ExcludeFromDescription();
}

app.MapDemoEndpoints();
app.MapAuthEndpoints();
app.MapListingEndpoints();
app.MapPhotoEndpoints();
app.MapBookingEndpoints();
app.MapLedgerEndpoints();
app.MapHandoverEndpoints();
app.MapAdminEndpoints();

app.MapHealthChecks("/health/live", new HealthCheckOptions { Predicate = _ => false });
app.MapHealthChecks("/health/ready", new HealthCheckOptions { Predicate = check => check.Tags.Contains("ready") });
if (serveWeb)
{
    // Every other path is a page of the web app; unknown API paths stay 404s.
    app.MapFallback(async context =>
    {
        if (WebHeadersMiddleware.IsApi(context.Request.Path))
        {
            context.Response.StatusCode = StatusCodes.Status404NotFound;
            return;
        }

        context.Response.ContentType = "text/html; charset=utf-8";
        context.Response.Headers.CacheControl = "no-cache";
        await context.Response.SendFileAsync(indexPage, context.RequestAborted);
    });
}

if (app.Configuration.GetValue("Database:MigrateOnStartup", false))
{
    await DependencyInjection.MigrateAsync(app.Services);
}

await StorageSetup.InitializeAsync(app.Services);

await app.RunAsync();

public partial class Program;
