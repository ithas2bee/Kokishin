using System.IdentityModel.Tokens.Jwt;
using System.Net;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text.Json;
using Kokishin.Api;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authentication.OpenIdConnect;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.IdentityModel.Protocols;
using Microsoft.IdentityModel.Protocols.OpenIdConnect;
using Microsoft.IdentityModel.Tokens;

namespace Kokishin.Tests;

public class AuthenticationTests
{
    [Fact]
    public async Task ValidBearerToken_MapsIssuerAndSubjectToStableInternalUserId()
    {
        using var authority = new TestOidcAuthority();
        using var factory = CreateFactory(authority);
        using var client = factory.CreateClient();
        var token = authority.CreateToken("provider-subject", nameIdentifier: "provider-supplied-id");

        using var firstResponse = await SendTokenAsync(client, token);
        var firstBody = await firstResponse.Content.ReadAsStringAsync();
        Assert.True(firstResponse.StatusCode == HttpStatusCode.OK, firstBody);
        using var firstIdentity = JsonDocument.Parse(firstBody);
        using var secondResponse = await SendTokenAsync(client, token);
        using var secondIdentity = JsonDocument.Parse(await secondResponse.Content.ReadAsStringAsync());
        var internalUserId = firstIdentity.RootElement.GetProperty("userId").GetString();

        Assert.Equal(HttpStatusCode.OK, firstResponse.StatusCode);
        Assert.Equal("provider-subject", firstIdentity.RootElement.GetProperty("subject").GetString());
        Assert.NotEqual("provider-subject", internalUserId);
        Assert.NotEqual("provider-supplied-id", internalUserId);
        Assert.True(Guid.TryParse(internalUserId, out _));
        Assert.Equal(internalUserId, secondIdentity.RootElement.GetProperty("userId").GetString());
    }

    [Fact]
    public async Task MissingCredentials_DoesNotTrustClientIdentityHeaders()
    {
        using var authority = new TestOidcAuthority();
        using var factory = CreateFactory(authority);
        using var client = factory.CreateClient();
        using var request = new HttpRequestMessage(HttpMethod.Get, "/test/identity");
        request.Headers.Add("X-User-Id", "client-controlled-user");

        using var response = await client.SendAsync(request);
        var body = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
        Assert.DoesNotContain("client-controlled-user", body);
    }

    [Fact]
    public async Task InvalidBearerTokens_AreRejected()
    {
        using var authority = new TestOidcAuthority();
        using var factory = CreateFactory(authority);
        using var client = factory.CreateClient();
        using var otherSigningKey = RSA.Create(2048);
        var invalidTokens = new[]
        {
            "not-a-jwt",
            authority.CreateToken(null),
            authority.CreateToken("expired", expires: DateTime.UtcNow.AddMinutes(-10)),
            authority.CreateToken("wrong-issuer", issuer: "https://untrusted.example.test/"),
            authority.CreateToken("wrong-audience", audience: "another-api"),
            authority.CreateToken("wrong-signature", signingKey: otherSigningKey)
        };

        foreach (var token in invalidTokens)
        {
            using var response = await SendTokenAsync(client, token);
            var body = await response.Content.ReadAsStringAsync();

            Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
            Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
            Assert.DoesNotContain(token, body);
        }
    }

    [Fact]
    public async Task LoginChallenge_UsesAuthorizationCodePkceAndConfiguredCallback()
    {
        using var authority = new TestOidcAuthority();
        using var factory = CreateFactory(authority);
        using var client = factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            AllowAutoRedirect = false
        });

        using var response = await client.GetAsync("/auth/login");
        var body = await response.Content.ReadAsStringAsync();
        Assert.True(response.StatusCode == HttpStatusCode.Redirect, body);
        var location = Assert.IsType<Uri>(response.Headers.Location);
        var query = Microsoft.AspNetCore.WebUtilities.QueryHelpers.ParseQuery(location.Query);
        var cookies = response.Headers.GetValues("Set-Cookie").ToArray();

        Assert.Equal("code", query["response_type"]);
        Assert.Equal("S256", query["code_challenge_method"]);
        Assert.Equal(TestOidcAuthority.CallbackUrl, query["redirect_uri"].ToString());
        Assert.DoesNotContain(TestOidcAuthority.ClientSecret, location.AbsoluteUri);
        Assert.NotEmpty(cookies);
        Assert.All(cookies, cookie =>
        {
            Assert.Contains("secure", cookie, StringComparison.OrdinalIgnoreCase);
            Assert.Contains("httponly", cookie, StringComparison.OrdinalIgnoreCase);
        });
    }

    [Fact]
    public async Task InvalidOidcCallbackState_ReturnsUnauthenticatedProblemDetails()
    {
        using var authority = new TestOidcAuthority();
        using var factory = CreateFactory(authority);
        using var client = factory.CreateClient();

        using var response = await client.GetAsync("/signin-oidc?code=invalid&state=invalid");
        var body = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
        Assert.DoesNotContain("invalid", body);
    }

    [Fact]
    public async Task CookieSession_IsOpaqueAndRequiresAntiforgeryForStateChanges()
    {
        using var authority = new TestOidcAuthority();
        using var factory = CreateFactory(authority);
        using var client = factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            BaseAddress = new Uri("https://localhost")
        });
        using var signInResponse = await client.GetAsync("/test/sign-in");
        var sessionCookie = GetCookie(signInResponse, "__Host-Kokishin.Session");

        using var csrfRequest = new HttpRequestMessage(HttpMethod.Get, "/auth/csrf");
        csrfRequest.Headers.TryAddWithoutValidation("Cookie", sessionCookie);
        using var csrfResponse = await client.SendAsync(csrfRequest);
        var csrfResponseBody = await csrfResponse.Content.ReadAsStringAsync();
        Assert.True(
            csrfResponse.StatusCode == HttpStatusCode.OK,
            csrfResponseBody);
        using var csrfBody = JsonDocument.Parse(csrfResponseBody);
        var requestToken = csrfBody.RootElement.GetProperty("requestToken").GetString();
        var antiforgeryCookie = GetCookie(csrfResponse, "__Host-Kokishin.Antiforgery");
        var cookies = $"{sessionCookie}; {antiforgeryCookie}";

        using var rejectedRequest = new HttpRequestMessage(HttpMethod.Post, "/test/state-change");
        rejectedRequest.Headers.TryAddWithoutValidation("Cookie", cookies);
        using var rejectedResponse = await client.SendAsync(rejectedRequest);
        var rejectedBody = await rejectedResponse.Content.ReadAsStringAsync();

        using var acceptedRequest = new HttpRequestMessage(HttpMethod.Post, "/test/state-change");
        acceptedRequest.Headers.TryAddWithoutValidation("Cookie", cookies);
        acceptedRequest.Headers.TryAddWithoutValidation("X-XSRF-TOKEN", requestToken);
        using var acceptedResponse = await client.SendAsync(acceptedRequest);

        Assert.Equal(HttpStatusCode.OK, signInResponse.StatusCode);
        Assert.DoesNotContain("provider-access-token", sessionCookie);
        Assert.Equal(HttpStatusCode.OK, csrfResponse.StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, rejectedResponse.StatusCode);
        Assert.Equal("application/problem+json", rejectedResponse.Content.Headers.ContentType?.MediaType);
        Assert.Equal(HttpStatusCode.OK, acceptedResponse.StatusCode);
        Assert.Equal("state changed", await acceptedResponse.Content.ReadAsStringAsync());
    }

    [Fact]
    public void ProductionWithoutAuthenticationConfiguration_FailsAtStartup()
    {
        using var factory = new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
        {
            builder.UseEnvironment("Production");
            builder.UseSetting("ConnectionStrings:Kokishin", "Data Source=:memory:");
        });

        var exception = Assert.Throws<InvalidOperationException>(() => factory.CreateClient());

        Assert.Contains("Authentication:Authority", exception.Message);
        Assert.Contains("Authentication:ClientSecret", exception.Message);
    }

    private static async Task<HttpResponseMessage> SendTokenAsync(HttpClient client, string token)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, "/test/identity");
        request.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", token);
        return await client.SendAsync(request);
    }

    private static string GetCookie(HttpResponseMessage response, string name) =>
        response.Headers.GetValues("Set-Cookie")
            .Select(cookie => cookie.Split(';', 2)[0])
            .Single(cookie => cookie.StartsWith($"{name}=", StringComparison.Ordinal));

    private static WebApplicationFactory<Program> CreateFactory(TestOidcAuthority authority) =>
        new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
        {
            builder.UseEnvironment("Production");
            builder.UseSetting("ConnectionStrings:Kokishin", "Data Source=:memory:");
            builder.UseSetting("Authentication:Authority", TestOidcAuthority.Issuer);
            builder.UseSetting("Authentication:Audience", TestOidcAuthority.Audience);
            builder.UseSetting("Authentication:ClientId", TestOidcAuthority.ClientId);
            builder.UseSetting("Authentication:ClientSecret", TestOidcAuthority.ClientSecret);
            builder.UseSetting("Authentication:CallbackUrl", TestOidcAuthority.CallbackUrl);
            builder.ConfigureTestServices(services =>
            {
                services.PostConfigure<JwtBearerOptions>("Kokishin.Bearer", options =>
                    options.ConfigurationManager = authority.CreateConfigurationManager());
                services.PostConfigure<OpenIdConnectOptions>(OpenIdConnectDefaults.AuthenticationScheme, options =>
                    options.ConfigurationManager = authority.CreateConfigurationManager());
                services.AddSingleton<IStartupFilter, AuthenticationProbeStartupFilter>();
            });
        });

    private sealed class AuthenticationProbeStartupFilter : IStartupFilter
    {
        public Action<IApplicationBuilder> Configure(Action<IApplicationBuilder> next) =>
            app =>
            {
                next(app);
                app.Use(async (context, nextMiddleware) =>
                {
                    if (context.Request.Path != "/test/identity")
                    {
                        if (context.Request.Path == "/test/sign-in")
                        {
                            var properties = new AuthenticationProperties();
                            properties.Items["kokishin:session_started_at"] =
                                DateTimeOffset.UtcNow.ToUnixTimeSeconds().ToString(
                                    System.Globalization.CultureInfo.InvariantCulture);
                            await context.SignInAsync(
                                "Kokishin.Cookie",
                                new ClaimsPrincipal(new ClaimsIdentity(
                                    [
                                        new Claim(ClaimTypes.NameIdentifier, "test-user-id"),
                                        new Claim("access_token", "provider-access-token")
                                    ],
                                    "test")),
                                properties);
                            return;
                        }

                        if (context.Request.Path == "/test/state-change")
                        {
                            await context.Response.WriteAsync("state changed");
                            return;
                        }

                        await nextMiddleware();
                        return;
                    }

                    var result = await context.AuthenticateAsync();
                    if (!result.Succeeded || result.Principal is null)
                    {
                        context.Response.StatusCode = StatusCodes.Status401Unauthorized;
                        return;
                    }

                    await context.Response.WriteAsJsonAsync(new
                    {
                        userId = result.Principal.FindFirst(ClaimTypes.NameIdentifier)?.Value,
                        subject = result.Principal.FindFirst("sub")?.Value
                    });
                });
            };
    }

    private sealed class TestOidcAuthority : IDisposable
    {
        public const string Issuer = "https://authentik.example.test/application/o/kokishin/";
        public const string Audience = "kokishin-api";
        public const string ClientId = "kokishin-web";
        public const string ClientSecret = "test-client-secret";
        public const string CallbackUrl = "https://api.example.test/signin-oidc";
        private const string KeyId = "test-signing-key";

        private readonly RSA _signingKey = RSA.Create(2048);

        public string CreateToken(
            string? subject,
            string? issuer = null,
            string? audience = null,
            DateTime? expires = null,
            RSA? signingKey = null,
            string? nameIdentifier = null)
        {
            var credentials = new SigningCredentials(
                new RsaSecurityKey(signingKey ?? _signingKey) { KeyId = KeyId },
                SecurityAlgorithms.RsaSha256);
            var claims = new List<Claim>();
            if (subject is not null)
            {
                claims.Add(new Claim("sub", subject));
            }

            if (nameIdentifier is not null)
            {
                claims.Add(new Claim(ClaimTypes.NameIdentifier, nameIdentifier));
            }

            var token = new JwtSecurityToken(
                issuer ?? Issuer,
                audience ?? Audience,
                claims,
                notBefore: expires < DateTime.UtcNow
                    ? expires?.AddMinutes(-5)
                    : DateTime.UtcNow.AddSeconds(-10),
                expires: expires ?? DateTime.UtcNow.AddMinutes(5),
                signingCredentials: credentials);
            return new JwtSecurityTokenHandler().WriteToken(token);
        }

        public void Dispose() => _signingKey.Dispose();

        public IConfigurationManager<OpenIdConnectConfiguration> CreateConfigurationManager()
        {
            var configuration = new OpenIdConnectConfiguration
            {
                Issuer = Issuer,
                AuthorizationEndpoint = $"{Issuer.TrimEnd('/')}/authorize/",
                TokenEndpoint = $"{Issuer.TrimEnd('/')}/token/",
                JwksUri = $"{Issuer.TrimEnd('/')}/jwks/"
            };
            configuration.SigningKeys.Add(new RsaSecurityKey(_signingKey) { KeyId = KeyId });
            return new StaticConfigurationManager<OpenIdConnectConfiguration>(configuration);
        }
    }
}
