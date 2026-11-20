using System.Text.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;

namespace Kokishin.Tests;

public class OpenApiTests
{
    [Fact]
    public async Task DatabaseHealthEndpoint_AnonymousRequest_ReturnsHealthyResponse()
    {
        using var factory = CreateFactory("Production");
        using var client = factory.CreateClient();
        using var request = new HttpRequestMessage(HttpMethod.Get, "/health/database");

        using var response = await client.SendAsync(request);
        var body = await response.Content.ReadAsStringAsync();

        Assert.Null(request.Headers.Authorization);
        Assert.Equal(System.Net.HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("text/plain", response.Content.Headers.ContentType?.MediaType);
        Assert.Equal("Healthy", body);
    }

    [Fact]
    public async Task OpenApiDocument_AnonymousRequestInDevelopment_DescribesDatabaseHealthEndpoint()
    {
        using var factory = CreateFactory("Development");
        using var client = factory.CreateClient();
        using var request = new HttpRequestMessage(HttpMethod.Get, "/openapi/v1.json");

        using var response = await client.SendAsync(request);
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var root = document.RootElement;
        var paths = root.GetProperty("paths");
        var get = paths.GetProperty("/health/database").GetProperty("get");
        var head = paths.GetProperty("/health/database").GetProperty("head");
        var profile = paths.GetProperty("/me/profile");
        var courses = paths.GetProperty("/courses");
        var course = courses.GetProperty("post");
        var courseList = courses.GetProperty("get");
        var courseDetails = paths.GetProperty("/courses/{courseId}").GetProperty("get");
        var courseUpdate = paths.GetProperty("/courses/{courseId}").GetProperty("put");
        var responses = get.GetProperty("responses");
        var responseCodes = responses.EnumerateObject().Select(item => item.Name).ToArray();

        Assert.Null(request.Headers.Authorization);
        Assert.Equal(System.Net.HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("Check database health", get.GetProperty("summary").GetString());
        Assert.True(profile.TryGetProperty("get", out _));
        Assert.True(profile.TryGetProperty("put", out _));
        Assert.Equal("Create a course owned by the authenticated Instructor",
            course.GetProperty("summary").GetString());
        Assert.True(course.GetProperty("responses").TryGetProperty("201", out _));
        Assert.True(course.GetProperty("responses").TryGetProperty("400", out _));
        Assert.True(course.GetProperty("responses").TryGetProperty("401", out _));
        Assert.True(course.GetProperty("responses").TryGetProperty("403", out _));
        Assert.Equal("List courses owned by the authenticated Instructor",
            courseList.GetProperty("summary").GetString());
        Assert.True(courseList.GetProperty("responses").TryGetProperty("200", out _));
        Assert.True(courseList.GetProperty("responses").TryGetProperty("401", out _));
        Assert.True(courseList.GetProperty("responses").TryGetProperty("403", out _));
        Assert.Equal("Get a course owned by the authenticated Instructor",
            courseDetails.GetProperty("summary").GetString());
        Assert.True(courseDetails.GetProperty("responses").TryGetProperty("200", out _));
        Assert.True(courseDetails.GetProperty("responses").TryGetProperty("401", out _));
        Assert.True(courseDetails.GetProperty("responses").TryGetProperty("403", out _));
        Assert.True(courseDetails.GetProperty("responses").TryGetProperty("404", out _));
        Assert.Equal("Update information for a course owned by the authenticated Instructor",
            courseUpdate.GetProperty("summary").GetString());
        Assert.True(courseUpdate.GetProperty("responses").TryGetProperty("200", out _));
        Assert.True(courseUpdate.GetProperty("responses").TryGetProperty("400", out _));
        Assert.True(courseUpdate.GetProperty("responses").TryGetProperty("401", out _));
        Assert.True(courseUpdate.GetProperty("responses").TryGetProperty("403", out _));
        Assert.True(courseUpdate.GetProperty("responses").TryGetProperty("404", out _));
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
        Assert.DoesNotContain("OpenApiSensitiveConfiguration", document.RootElement.ToString());
    }

    [Fact]
    public async Task OpenApiDocument_AnonymousRequestInProduction_IsDisabledByDefault()
    {
        using var factory = CreateFactory("Production");
        using var client = factory.CreateClient();
        using var request = new HttpRequestMessage(HttpMethod.Get, "/openapi/v1.json");

        using var response = await client.SendAsync(request);

        Assert.Null(request.Headers.Authorization);
        Assert.Equal(System.Net.HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task OpenApiDocument_AnonymousRequestInProduction_CanBeExplicitlyEnabled()
    {
        using var factory = CreateFactory("Production", exposeInProduction: true);
        using var client = factory.CreateClient();
        using var request = new HttpRequestMessage(HttpMethod.Get, "/openapi/v1.json");

        using var response = await client.SendAsync(request);
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());

        Assert.Null(request.Headers.Authorization);
        Assert.Equal(System.Net.HttpStatusCode.OK, response.StatusCode);
        Assert.True(document.RootElement.GetProperty("paths").TryGetProperty("/health/database", out _));
    }

    private static WebApplicationFactory<Program> CreateFactory(
        string environment,
        bool? exposeInProduction = null) =>
        new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
        {
            builder.UseEnvironment(environment);
            builder.UseSetting("ConnectionStrings:Kokishin", "Data Source=:memory:");
            builder.UseSetting("Authentication:Authority", "https://authentik.example.test/application/o/kokishin/");
            builder.UseSetting("Authentication:Audience", "kokishin-api");
            builder.UseSetting("Authentication:ClientId", "kokishin-web");
            builder.UseSetting("Authentication:ClientSecret", "test-client-secret");
            builder.UseSetting("Authentication:CallbackUrl", "https://api.example.test/signin-oidc");
            TestDataProtectionConfiguration.Configure(builder);
            if (exposeInProduction is not null)
            {
                builder.UseSetting("OpenApi:ExposeInProduction", exposeInProduction.Value.ToString());
            }
        });
}
