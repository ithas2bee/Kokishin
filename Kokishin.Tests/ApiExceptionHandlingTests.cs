using System.Collections.Concurrent;
using System.Text.Json;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Kokishin.Tests;

public class ApiExceptionHandlingTests : IClassFixture<WebApplicationFactory<Program>>
{
    private readonly WebApplicationFactory<Program> _factory;

    public ApiExceptionHandlingTests(WebApplicationFactory<Program> factory)
    {
        _factory = factory.WithWebHostBuilder(builder =>
        {
            builder.UseEnvironment("Production");
            builder.UseSetting("ConnectionStrings:Kokishin", "Data Source=:memory:");
            builder.ConfigureTestServices(services =>
                services.AddSingleton<IStartupFilter, ExceptionEndpointStartupFilter>());
        });
    }

    [Fact]
    public async Task UnhandledException_ReturnsSafeInternalServerError()
    {
        using var client = _factory.CreateClient();

        using var response = await client.GetAsync("/test/exception");
        var body = await response.Content.ReadAsStringAsync();
        using var problem = JsonDocument.Parse(body);

        Assert.Equal(System.Net.HttpStatusCode.InternalServerError, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
        Assert.Equal(500, problem.RootElement.GetProperty("status").GetInt32());
        Assert.Equal("An unexpected error occurred.", problem.RootElement.GetProperty("title").GetString());
        Assert.Equal("The server encountered an unexpected error.", problem.RootElement.GetProperty("detail").GetString());
        Assert.False(string.IsNullOrWhiteSpace(problem.RootElement.GetProperty("traceId").GetString()));
        Assert.DoesNotContain("Sensitive exception detail.", body);
        Assert.DoesNotContain("InvalidOperationException", body);
        Assert.DoesNotContain(" at ", body);
    }

    [Fact]
    public async Task UnhandledException_IsLoggedWithExceptionDetails()
    {
        var loggerProvider = new CapturingLoggerProvider();
        using var factory = _factory.WithWebHostBuilder(builder =>
            builder.ConfigureLogging(logging => logging.AddProvider(loggerProvider)));
        using var client = factory.CreateClient();

        using var response = await client.GetAsync("/test/exception");

        Assert.Equal(System.Net.HttpStatusCode.InternalServerError, response.StatusCode);
        var entry = Assert.Single(loggerProvider.Entries, entry => entry.Exception is not null);
        Assert.Equal(LogLevel.Error, entry.Level);
        Assert.IsType<InvalidOperationException>(entry.Exception);
        Assert.Equal("Sensitive exception detail.", entry.Exception.Message);
    }

    [Fact]
    public void LoggingLevel_CanBeOverriddenThroughConfiguration()
    {
        using var factory = new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
        {
            builder.UseEnvironment("Production");
            builder.UseSetting("ConnectionStrings:Kokishin", "Data Source=:memory:");
            builder.ConfigureAppConfiguration((_, configuration) =>
                configuration.Add(new IsolatedEnvironmentVariablesConfigurationSource(
                    [new("Logging__LogLevel__Default", "Trace")])));
        });
        using var client = factory.CreateClient();
        var logger = factory.Services.GetRequiredService<ILoggerFactory>().CreateLogger("LoggingConfigurationTest");

        Assert.True(logger.IsEnabled(LogLevel.Trace));
    }

    [Fact]
    public async Task ValidationFailure_ReturnsProblemDetailsDistinctFromServerError()
    {
        using var client = _factory.CreateClient();

        using var response = await client.GetAsync("/test/validation");
        using var problem = JsonDocument.Parse(await response.Content.ReadAsStringAsync());

        Assert.Equal(System.Net.HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
        Assert.Equal(400, problem.RootElement.GetProperty("status").GetInt32());
        Assert.Contains("Name", problem.RootElement.GetProperty("errors").EnumerateObject().Select(error => error.Name));
        Assert.False(string.IsNullOrWhiteSpace(problem.RootElement.GetProperty("traceId").GetString()));
    }

    [Fact]
    public async Task MissingRoute_ReturnsProblemDetails()
    {
        using var client = _factory.CreateClient();

        using var response = await client.GetAsync("/test/missing");
        using var problem = JsonDocument.Parse(await response.Content.ReadAsStringAsync());

        Assert.Equal(System.Net.HttpStatusCode.NotFound, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
        Assert.Equal(404, problem.RootElement.GetProperty("status").GetInt32());
        Assert.False(string.IsNullOrWhiteSpace(problem.RootElement.GetProperty("title").GetString()));
        Assert.False(string.IsNullOrWhiteSpace(problem.RootElement.GetProperty("traceId").GetString()));
    }

    [Fact]
    public async Task DatabaseHealthCheck_RemainsAvailable()
    {
        using var client = _factory.CreateClient();

        using var response = await client.GetAsync("/health/database");
        var body = await response.Content.ReadAsStringAsync();

        Assert.Equal(System.Net.HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("Healthy", body);
    }

    private sealed class ExceptionEndpointStartupFilter : IStartupFilter
    {
        public Action<IApplicationBuilder> Configure(Action<IApplicationBuilder> next) =>
            app =>
            {
                next(app);
                app.Use(async (context, nextMiddleware) =>
                {
                    if (context.Request.Path == "/test/exception")
                    {
                        throw new InvalidOperationException("Sensitive exception detail.");
                    }

                    if (context.Request.Path == "/test/validation")
                    {
                        await Results.ValidationProblem(new Dictionary<string, string[]>
                        {
                            ["Name"] = ["The Name field is required."]
                        }).ExecuteAsync(context);
                        return;
                    }

                    await nextMiddleware(context);
                });
            };
    }

    private sealed class CapturingLoggerProvider : ILoggerProvider
    {
        private readonly ConcurrentQueue<CapturedLogEntry> _entries = new();

        public IReadOnlyCollection<CapturedLogEntry> Entries => _entries.ToArray();

        public ILogger CreateLogger(string categoryName) => new CapturingLogger(categoryName, _entries);

        public void Dispose()
        {
        }

        private sealed class CapturingLogger(
            string categoryName,
            ConcurrentQueue<CapturedLogEntry> entries) : ILogger
        {
            public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

            public bool IsEnabled(LogLevel logLevel) => logLevel != LogLevel.None;

            public void Log<TState>(
                LogLevel logLevel,
                EventId eventId,
                TState state,
                Exception? exception,
                Func<TState, Exception?, string> formatter)
            {
                entries.Enqueue(new CapturedLogEntry(
                    categoryName,
                    logLevel,
                    formatter(state, exception),
                    exception));
            }
        }
    }

    private sealed record CapturedLogEntry(
        string Category,
        LogLevel Level,
        string Message,
        Exception? Exception);

    private sealed class IsolatedEnvironmentVariablesConfigurationSource(
        IEnumerable<KeyValuePair<string, string?>> variables) : IConfigurationSource
    {
        public IConfigurationProvider Build(IConfigurationBuilder builder) =>
            new IsolatedEnvironmentVariablesConfigurationProvider(variables);
    }

    private sealed class IsolatedEnvironmentVariablesConfigurationProvider(
        IEnumerable<KeyValuePair<string, string?>> variables) : ConfigurationProvider
    {
        public override void Load() =>
            Data = variables.ToDictionary(
                variable => variable.Key.Replace("__", ConfigurationPath.KeyDelimiter, StringComparison.Ordinal),
                variable => variable.Value,
                StringComparer.OrdinalIgnoreCase);
    }
}
