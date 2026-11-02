using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;

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

        Assert.Equal(System.Net.HttpStatusCode.InternalServerError, response.StatusCode);
        Assert.Equal("An unexpected error occurred.", body);
        Assert.DoesNotContain("Sensitive exception detail.", body);
        Assert.DoesNotContain("InvalidOperationException", body);
        Assert.DoesNotContain(" at ", body);
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

                    await nextMiddleware(context);
                });
            };
    }
}
