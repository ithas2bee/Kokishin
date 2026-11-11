using System.Diagnostics;
using Kokishin.Application;
using Kokishin.Api;
using Kokishin.Infrastructure;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.OpenIdConnect;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.OpenApi;

var builder = WebApplication.CreateBuilder(args);
builder.Services.AddApplication();
builder.Services.AddInfrastructure(builder.Configuration);
var authenticationSettings = AuthentikAuthenticationSettings.Read(builder.Configuration, builder.Environment);
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
    app.MapGet("/auth/login", (HttpContext context) =>
        Results.Challenge(
            new AuthenticationProperties { RedirectUri = "/" },
            [OpenIdConnectDefaults.AuthenticationScheme]))
        .AllowAnonymous()
        .ExcludeFromDescription();
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

public partial class Program;
