using Kokishin.Application;
using Kokishin.Infrastructure;

var builder = WebApplication.CreateBuilder(args);
builder.Services.AddApplication();
builder.Services.AddInfrastructure(builder.Configuration);
builder.Services.AddProblemDetails(options =>
{
    options.CustomizeProblemDetails = context =>
    {
        context.ProblemDetails.Extensions["traceId"] = context.HttpContext.TraceIdentifier;

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
app.MapHealthChecks("/health/database");

app.Run();

public partial class Program;
