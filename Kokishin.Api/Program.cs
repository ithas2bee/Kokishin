using System.Diagnostics;
using System.Security.Claims;
using Kokishin.Application;
using Kokishin.Api;
using Kokishin.Application.Profiles;
using Kokishin.Infrastructure;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.OpenIdConnect;
using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.OpenApi;

var builder = WebApplication.CreateBuilder(args);
builder.Services.AddApplication();
builder.Services.AddInfrastructure(builder.Configuration);
var authenticationSettings = AuthentikAuthenticationSettings.Read(builder.Configuration, builder.Environment);
var dataProtectionSettings = PersistentDataProtectionSettings.Read(
    builder.Configuration,
    builder.Environment,
    authenticationSettings is not null);
if (dataProtectionSettings is not null)
{
    builder.Services.AddDataProtection()
        .PersistKeysToFileSystem(dataProtectionSettings.KeyStorageDirectory)
        .ProtectKeysWithCertificate(dataProtectionSettings.KeyEncryptionCertificate)
        .SetApplicationName("Kokishin");
    builder.Services.AddSingleton(dataProtectionSettings.KeyEncryptionCertificate);
}
builder.Services.AddAuthentication();
builder.Services.AddAuthorization();
if (authenticationSettings is not null)
{
    builder.Services.AddAuthentikAuthentication(authenticationSettings);
}
builder.Services.AddOpenApi(options =>
    options.AddSchemaTransformer((schema, context, _) =>
    {
        if (context.JsonTypeInfo?.Type == typeof(ProblemDetails))
        {
            schema.Properties ??= new Dictionary<string, IOpenApiSchema>();
            schema.Properties["traceId"] = new OpenApiSchema { Type = JsonSchemaType.String };
        }

        return Task.CompletedTask;
    }));
builder.Logging.Configure(options =>
    options.ActivityTrackingOptions =
        ActivityTrackingOptions.TraceId |
        ActivityTrackingOptions.SpanId |
        ActivityTrackingOptions.ParentId);
builder.Services.AddProblemDetails(options =>
{
    options.CustomizeProblemDetails = context =>
    {
        var activity = Activity.Current;
        context.ProblemDetails.Extensions["traceId"] =
            activity is { IdFormat: ActivityIdFormat.W3C } && activity.TraceId != default
                ? activity.TraceId.ToString()
                : context.HttpContext.TraceIdentifier;

        if (context.ProblemDetails.Status == StatusCodes.Status500InternalServerError)
        {
            context.ProblemDetails.Title = "An unexpected error occurred.";
            context.ProblemDetails.Detail = "The server encountered an unexpected error.";
        }
    };
});

var app = builder.Build();

app.UseExceptionHandler();
app.UseStatusCodePages();
app.UseAuthentication();
app.UseAuthorization();
if (authenticationSettings is not null)
{
    app.UseCookieCsrfProtection();
}

if (authenticationSettings is not null)
{
    app.MapGet("/auth/login", (HttpContext context) =>
        Results.Challenge(
            new AuthenticationProperties { RedirectUri = "/" },
            [OpenIdConnectDefaults.AuthenticationScheme]))
        .AllowAnonymous()
        .ExcludeFromDescription();

    app.MapGet("/auth/csrf", (HttpContext context, IAntiforgery antiforgery) =>
        Results.Ok(new { requestToken = antiforgery.GetAndStoreTokens(context).RequestToken }))
        .RequireAuthorization()
        .ExcludeFromDescription();

    app.MapGet("/me/profile", GetOwnProfileAsync)
        .RequireAuthorization()
        .WithSummary("Get the authenticated user's profile")
        .Produces<ProfileResponse>()
        .ProducesProblem(StatusCodes.Status401Unauthorized)
        .ProducesProblem(StatusCodes.Status404NotFound);

    app.MapPut("/me/profile", UpdateOwnProfileAsync)
        .RequireAuthorization()
        .WithSummary("Create or update the authenticated user's profile")
        .Produces<ProfileResponse>()
        .ProducesValidationProblem()
        .ProducesProblem(StatusCodes.Status401Unauthorized);
}

app.MapMethods("/health/database", [HttpMethods.Get, HttpMethods.Head], async (
        HealthCheckService healthCheckService,
        HttpContext context) =>
    {
        var report = await healthCheckService.CheckHealthAsync(context.RequestAborted);
        context.Response.StatusCode = report.Status == HealthStatus.Unhealthy
            ? StatusCodes.Status503ServiceUnavailable
            : StatusCodes.Status200OK;
        context.Response.ContentType = "text/plain";
        await context.Response.WriteAsync(report.Status.ToString(), context.RequestAborted);
    })
    .WithSummary("Check database health")
    .WithDescription("Reports whether the configured database is healthy.")
    .Produces<string>(StatusCodes.Status200OK, contentType: "text/plain")
    .Produces<string>(StatusCodes.Status503ServiceUnavailable, contentType: "text/plain")
    .ProducesProblem(StatusCodes.Status500InternalServerError);

if (app.Environment.IsDevelopment() ||
    (app.Environment.IsProduction() && app.Configuration.GetValue<bool>("OpenApi:ExposeInProduction")))
{
    app.MapOpenApi();
}

app.Run();

static async Task<IResult> GetOwnProfileAsync(HttpContext context, IProfileService profileService)
{
    if (!TryGetAccountId(context.User, out var accountId))
    {
        return Results.Forbid();
    }

    var profile = await profileService.GetOwnProfileAsync(accountId, context.RequestAborted);
    return profile is null
        ? Results.NotFound()
        : Results.Ok(new ProfileResponse(profile.DisplayName, profile.Description));
}

static async Task<IResult> UpdateOwnProfileAsync(
    HttpContext context,
    UpdateProfileRequest request,
    IProfileService profileService)
{
    if (!TryGetAccountId(context.User, out var accountId))
    {
        return Results.Forbid();
    }

    try
    {
        var profile = await profileService.UpdateOwnProfileAsync(
            accountId,
            request.DisplayName,
            request.Description,
            context.RequestAborted);
        return Results.Ok(new ProfileResponse(profile.DisplayName, profile.Description));
    }
    catch (ArgumentException exception) when (exception.ParamName is "displayName" or "description")
    {
        var field = exception.ParamName!;
        return Results.ValidationProblem(new Dictionary<string, string[]>
        {
            [field] = [exception.Message]
        });
    }
}

static bool TryGetAccountId(ClaimsPrincipal principal, out Guid accountId) =>
    Guid.TryParse(principal.FindFirstValue(ClaimTypes.NameIdentifier), out accountId);

internal sealed record UpdateProfileRequest(string DisplayName, string? Description);

internal sealed record ProfileResponse(string DisplayName, string? Description);

public partial class Program;
