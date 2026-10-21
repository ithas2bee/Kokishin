using Kokishin.Infrastructure;

var builder = WebApplication.CreateBuilder(args);
builder.Services.AddInfrastructure(builder.Configuration);

var app = builder.Build();

app.MapHealthChecks("/health/database");

app.Run();
