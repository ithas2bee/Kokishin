using System.Text.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;

namespace Kokishin.Tests;

public class OpenApiTests
{
    [Fact]
    public async Task OpenApiDocument_InDevelopment_DescribesDatabaseHealthEndpoint()
    {
        using var factory = CreateFactory("Development");
        using var client = factory.CreateClient();

        using var response = await client.GetAsync("/openapi/v1.json");
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var root = document.RootElement;
        var paths = root.GetProperty("paths");
        var get = paths.GetProperty("/health/database").GetProperty("get");
        var head = paths.GetProperty("/health/database").GetProperty("head");
        var responses = get.GetProperty("responses");
        var responseCodes = responses.EnumerateObject().Select(item => item.Name).ToArray();

        Assert.Equal(System.Net.HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("Check database health", get.GetProperty("summary").GetString());
        Assert.Contains("200", responseCodes);
        Assert.Contains("503", responseCodes);
        Assert.Contains("500", responseCodes);
        Assert.Equal("string", responses.GetProperty("200").GetProperty("content")
            .GetProperty("text/plain").GetProperty("schema").GetProperty("type").GetString());
        Assert.Equal("string", responses.GetProperty("503").GetProperty("content")
            .GetProperty("text/plain").GetProperty("schema").GetProperty("type").GetString());
        var problemSchema = responses.GetProperty("500").GetProperty("content")
            .GetProperty("application/problem+json").GetProperty("schema");
        Assert.EndsWith("/ProblemDetails", problemSchema.GetProperty("$ref").GetString());
        Assert.Equal("string", root.GetProperty("components").GetProperty("schemas")
            .GetProperty("ProblemDetails").GetProperty("properties").GetProperty("traceId")
            .GetProperty("type").GetString());
        Assert.True(head.GetProperty("responses").TryGetProperty("200", out _));
        Assert.Single(paths.EnumerateObject());
        Assert.DoesNotContain("OpenApiSensitiveConfiguration", document.RootElement.ToString());
    }

    [Fact]
    public async Task OpenApiDocument_InProduction_IsDisabledByDefault()
    {
        using var factory = CreateFactory("Production");
        using var client = factory.CreateClient();

        using var response = await client.GetAsync("/openapi/v1.json");

        Assert.Equal(System.Net.HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task OpenApiDocument_InProduction_CanBeExplicitlyEnabled()
    {
        using var factory = CreateFactory("Production", exposeInProduction: true);
        using var client = factory.CreateClient();

        using var response = await client.GetAsync("/openapi/v1.json");
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());

        Assert.Equal(System.Net.HttpStatusCode.OK, response.StatusCode);
        Assert.True(document.RootElement.GetProperty("paths").TryGetProperty("/health/database", out _));
    }

    private static WebApplicationFactory<Program> CreateFactory(
        string environment,
        bool? exposeInProduction = null) =>
        new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
        {
            builder.UseEnvironment(environment);
            builder.UseSetting("ConnectionStrings:Kokishin", "Data Source=OpenApiSensitiveConfiguration");
            if (exposeInProduction is not null)
            {
                builder.UseSetting("OpenApi:ExposeInProduction", exposeInProduction.Value.ToString());
            }
        });
}
