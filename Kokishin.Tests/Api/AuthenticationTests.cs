using System.IdentityModel.Tokens.Jwt;
using System.Net;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text.Json;
using System.Net.Http.Json;
using Kokishin.Application.Accounts;
using Kokishin.Api;
using Kokishin.Domain.Accounts;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authentication.OpenIdConnect;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
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
    public async Task InactiveAccount_ValidBearerTokenIsRejected()
    {
        using var authority = new TestOidcAuthority();
        using var factory = CreateFactory(authority);
        using var client = factory.CreateClient();
        var lifecycle = factory.Services.GetRequiredService<IAccountLifecycleService>();
        var provisioning = await lifecycle.ProvisionVerifiedIdentityAsync(
            TestOidcAuthority.Issuer,
            "inactive-subject",
            AccountRole.Learner);

        using var response = await SendTokenAsync(client, authority.CreateToken("inactive-subject"));

        Assert.Equal(AccountStatus.Inactive, provisioning.Account.Status);
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
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
    public async Task ProfileEndpoints_RequireAuthentication()
    {
        using var authority = new TestOidcAuthority();
        using var factory = CreateFactory(authority);
        using var client = factory.CreateClient();

        using var getResponse = await client.GetAsync("/me/profile");
        using var updateResponse = await client.PutAsJsonAsync(
            "/me/profile",
            new { displayName = "Ada", description = (string?)null });

        Assert.Equal(HttpStatusCode.Unauthorized, getResponse.StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, updateResponse.StatusCode);
    }

    [Fact]
    public async Task InstructorPolicy_UnauthenticatedCallerGetsUnauthorized()
    {
        using var authority = new TestOidcAuthority();
        using var factory = CreateFactory(authority);
        using var client = factory.CreateClient();
        using var request = new HttpRequestMessage(
            HttpMethod.Post,
            "/test/authorize/instructor?role=Instructor")
        {
            Content = JsonContent.Create(new { role = "Instructor" })
        };
        request.Headers.Add("X-Role", "Instructor");

        using var response = await client.SendAsync(request);
        using var problem = JsonDocument.Parse(await response.Content.ReadAsStringAsync());

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
        Assert.Equal((int)HttpStatusCode.Unauthorized, problem.RootElement.GetProperty("status").GetInt32());
    }

    [Fact]
    public async Task InstructorPolicy_LearnerCannotGainRoleFromClaimsOrRequestData()
    {
        using var authority = new TestOidcAuthority();
        using var factory = CreateFactory(authority);
        using var client = factory.CreateClient();
        using var request = new HttpRequestMessage(
            HttpMethod.Post,
            "/test/authorize/instructor?role=Instructor")
        {
            Content = JsonContent.Create(new { role = "Instructor" })
        };
        request.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue(
            "Bearer",
            authority.CreateToken("provider-subject", role: "Instructor"));
        request.Headers.Add("X-Role", "Instructor");

        using var response = await client.SendAsync(request);
        using var problem = JsonDocument.Parse(await response.Content.ReadAsStringAsync());

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
        Assert.Equal((int)HttpStatusCode.Forbidden, problem.RootElement.GetProperty("status").GetInt32());
    }

    [Theory]
    [InlineData(AccountRole.Instructor)]
    [InlineData(AccountRole.Learner)]
    public async Task RolePolicy_TrustedProvisionedRoleIsAuthorized(AccountRole role)
    {
        using var authority = new TestOidcAuthority();
        using var factory = CreateFactory(authority);
        using var client = factory.CreateClient();
        var subject = $"policy-{role}";
        var lifecycle = factory.Services.GetRequiredService<IAccountLifecycleService>();
        var provisioning = await lifecycle.ProvisionVerifiedIdentityAsync(
            TestOidcAuthority.Issuer,
            subject,
            role);
        await lifecycle.ActivateAsync(provisioning.Account.Id);
        var path = role == AccountRole.Instructor
            ? "/test/authorize/instructor"
            : "/test/authorize/learner";

        using var response = await SendRoleAuthorizationRequestAsync(
            client,
            path,
            authority.CreateToken(subject));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("authorized", await response.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task ProfileEndpoints_OnlyReadAndUpdateAuthenticatedAccountProfile()
    {
        using var authority = new TestOidcAuthority();
        using var factory = CreateFactory(authority);
        using var client = factory.CreateClient();
        var firstToken = authority.CreateToken(
            "provider-subject",
            nameIdentifier: Guid.NewGuid().ToString("D"));

        using var firstUpdate = await SendProfileRequestAsync(
            client,
            firstToken,
            HttpMethod.Put,
            new
            {
                displayName = "Ada",
                description = "Instructor",
                role = "Instructor",
                accountId = Guid.NewGuid(),
                issuer = "https://attacker.example.test/",
                subject = "replacement-subject",
                status = (int)AccountStatus.Inactive
            });
        using var firstGet = await SendProfileRequestAsync(client, firstToken, HttpMethod.Get);
        using var firstProfile = JsonDocument.Parse(await firstGet.Content.ReadAsStringAsync());

        var lifecycle = factory.Services.GetRequiredService<IAccountLifecycleService>();
        var secondAccount = await lifecycle.ProvisionVerifiedIdentityAsync(
            TestOidcAuthority.Issuer,
            "second-provider-subject",
            AccountRole.Learner);
        await lifecycle.ActivateAsync(secondAccount.Account.Id);
        var secondToken = authority.CreateToken("second-provider-subject");
        using var secondInitialGet = await SendProfileRequestAsync(client, secondToken, HttpMethod.Get);
        using var secondUpdate = await SendProfileRequestAsync(
            client,
            secondToken,
            HttpMethod.Put,
            new { displayName = "Grace", description = (string?)null });
        using var firstAfterSecondUpdate = await SendProfileRequestAsync(
            client,
            firstToken,
            HttpMethod.Get);
        using var firstProfileAfterSecondUpdate =
            JsonDocument.Parse(await firstAfterSecondUpdate.Content.ReadAsStringAsync());
        using var databaseScope = factory.Services.CreateScope();
        var database = databaseScope.ServiceProvider.GetRequiredService<
            Kokishin.Infrastructure.Persistence.KokishinDbContext>();
        var originalAccount = await database.Accounts.SingleAsync(account => account.Id == ActiveAccountId);

        Assert.Equal(HttpStatusCode.OK, firstUpdate.StatusCode);
        Assert.Equal(HttpStatusCode.OK, firstGet.StatusCode);
        Assert.Equal("Ada", firstProfile.RootElement.GetProperty("displayName").GetString());
        Assert.Equal("Instructor", firstProfile.RootElement.GetProperty("description").GetString());
        Assert.False(firstProfile.RootElement.TryGetProperty("accountId", out _));
        Assert.False(firstProfile.RootElement.TryGetProperty("issuer", out _));
        Assert.False(firstProfile.RootElement.TryGetProperty("subject", out _));
        Assert.False(firstProfile.RootElement.TryGetProperty("status", out _));
        Assert.False(firstProfile.RootElement.TryGetProperty("role", out _));
        Assert.Equal(HttpStatusCode.NotFound, secondInitialGet.StatusCode);
        Assert.Equal(HttpStatusCode.OK, secondUpdate.StatusCode);
        Assert.Equal(HttpStatusCode.OK, firstAfterSecondUpdate.StatusCode);
        Assert.Equal("Ada", firstProfileAfterSecondUpdate.RootElement.GetProperty("displayName").GetString());
        Assert.Equal(TestOidcAuthority.Issuer, originalAccount.Issuer);
        Assert.Equal("provider-subject", originalAccount.Subject);
        Assert.Equal(AccountStatus.Active, originalAccount.Status);
        Assert.Equal(AccountRole.Learner, originalAccount.Role);
    }

    [Fact]
    public async Task ProfileUpdate_InvalidDisplayName_ReturnsValidationProblem()
    {
        using var authority = new TestOidcAuthority();
        using var factory = CreateFactory(authority);
        using var client = factory.CreateClient();

        using var response = await SendProfileRequestAsync(
            client,
            authority.CreateToken("provider-subject"),
            HttpMethod.Put,
            new { displayName = " ", description = (string?)null });
        using var problem = JsonDocument.Parse(await response.Content.ReadAsStringAsync());

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Contains(
            "displayName",
            problem.RootElement.GetProperty("errors").EnumerateObject().Select(error => error.Name));
    }

    private static Guid ActiveAccountId =>
        new Account(TestOidcAuthority.Issuer, "provider-subject", AccountRole.Learner).Id;

    private static async Task<HttpResponseMessage> SendProfileRequestAsync(
        HttpClient client,
        string token,
        HttpMethod method,
        object? body = null)
    {
        using var request = new HttpRequestMessage(method, "/me/profile");
        request.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", token);
        if (body is not null)
        {
            request.Content = JsonContent.Create(body);
        }

        return await client.SendAsync(request);
    }

    private static async Task<HttpResponseMessage> SendRoleAuthorizationRequestAsync(
        HttpClient client,
        string path,
        string token)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, path);
        request.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", token);
        return await client.SendAsync(request);
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
    public async Task DeactivatedCookieAccount_CannotContinueUsingExistingSession()
    {
        using var authority = new TestOidcAuthority();
        using var factory = CreateFactory(authority);
        using var client = factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            BaseAddress = new Uri("https://localhost")
        });
        using var signInResponse = await client.GetAsync("/test/sign-in");
        var sessionCookie = GetCookie(signInResponse, "__Host-Kokishin.Session");
        await factory.Services.GetRequiredService<IAccountLifecycleService>()
            .DeactivateAsync(ActiveAccountId);

        using var request = new HttpRequestMessage(HttpMethod.Get, "/test/identity");
        request.Headers.TryAddWithoutValidation("Cookie", sessionCookie);
        using var response = await client.SendAsync(request);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
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

    [Fact]
    public void ProductionWithAuthenticationButMissingPersistentDataProtectionSettings_FailsAtStartup()
    {
        using var factory = new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
        {
            builder.UseEnvironment("Production");
            builder.UseSetting("ConnectionStrings:Kokishin", "Data Source=:memory:");
            builder.UseSetting("Authentication:Authority", TestOidcAuthority.Issuer);
            builder.UseSetting("Authentication:Audience", TestOidcAuthority.Audience);
            builder.UseSetting("Authentication:ClientId", TestOidcAuthority.ClientId);
            builder.UseSetting("Authentication:ClientSecret", TestOidcAuthority.ClientSecret);
            builder.UseSetting("Authentication:CallbackUrl", TestOidcAuthority.CallbackUrl);
        });

        var exception = Assert.Throws<InvalidOperationException>(() => factory.CreateClient());

        Assert.Contains("DataProtection:KeysPath", exception.Message);
        Assert.Contains("DataProtection:CertificatePassword", exception.Message);
    }

    [Fact]
    public async Task DevelopmentWithoutAuthenticationConfiguration_KeepsPublicEndpointsAvailable()
    {
        using var factory = new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
        {
            builder.UseEnvironment("Development");
            builder.UseSetting("ConnectionStrings:Kokishin", "Data Source=:memory:");
        });
        using var client = factory.CreateClient();

        using var healthResponse = await client.GetAsync("/health/database");
        using var openApiResponse = await client.GetAsync("/openapi/v1.json");

        Assert.Equal(HttpStatusCode.OK, healthResponse.StatusCode);
        Assert.Equal(HttpStatusCode.OK, openApiResponse.StatusCode);
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

    private static AuthenticationTestFactory CreateFactory(TestOidcAuthority authority) =>
        new AuthenticationTestFactory(authority);

    [Fact]
    public async Task CookieSession_SurvivesApiHostRecreation()
    {
        using var authority = new TestOidcAuthority();
        var databasePath = Path.Combine(Path.GetTempPath(), $"kokishin-sessions-{Guid.NewGuid():N}.db");
        try
        {
            string sessionCookie;
            using (var firstFactory = new AuthenticationTestFactory(
                       authority,
                       databasePath,
                       deleteDatabaseOnDispose: false))
            {
                using var firstClient = firstFactory.CreateClient(new WebApplicationFactoryClientOptions
                {
                    BaseAddress = new Uri("https://localhost")
                });
                using var signInResponse = await firstClient.GetAsync("/test/sign-in");
                sessionCookie = GetCookie(signInResponse, "__Host-Kokishin.Session");
            }

            using (var secondFactory = new AuthenticationTestFactory(authority, databasePath))
            using (var secondClient = secondFactory.CreateClient(new WebApplicationFactoryClientOptions
                   {
                       BaseAddress = new Uri("https://localhost")
                   }))
            using (var request = new HttpRequestMessage(HttpMethod.Get, "/test/identity"))
            {
                request.Headers.TryAddWithoutValidation("Cookie", sessionCookie);
                using var response = await secondClient.SendAsync(request);
                using var identity = JsonDocument.Parse(await response.Content.ReadAsStringAsync());

                Assert.Equal(HttpStatusCode.OK, response.StatusCode);
                Assert.Equal(ActiveAccountId.ToString("D"), identity.RootElement.GetProperty("userId").GetString());
            }
        }
        finally
        {
            File.Delete(databasePath);
        }
    }

    private sealed class AuthenticationTestFactory : WebApplicationFactory<Program>
    {
        private readonly TestOidcAuthority _authority;
        private readonly string _databasePath;
        private readonly bool _deleteDatabaseOnDispose;

        public AuthenticationTestFactory(
            TestOidcAuthority authority,
            string? databasePath = null,
            bool deleteDatabaseOnDispose = true)
        {
            _authority = authority;
            _databasePath = databasePath ??
                Path.Combine(Path.GetTempPath(), $"kokishin-auth-{Guid.NewGuid():N}.db");
            _deleteDatabaseOnDispose = deleteDatabaseOnDispose;
            InitializeDatabase();
        }

        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            builder.UseEnvironment("Production");
            builder.UseSetting("ConnectionStrings:Kokishin", $"Data Source={_databasePath}");
            builder.UseSetting("Authentication:Authority", TestOidcAuthority.Issuer);
            builder.UseSetting("Authentication:Audience", TestOidcAuthority.Audience);
            builder.UseSetting("Authentication:ClientId", TestOidcAuthority.ClientId);
            builder.UseSetting("Authentication:ClientSecret", TestOidcAuthority.ClientSecret);
            builder.UseSetting("Authentication:CallbackUrl", TestOidcAuthority.CallbackUrl);
            TestDataProtectionConfiguration.Configure(builder);
            builder.ConfigureTestServices(services =>
            {
                services.PostConfigure<JwtBearerOptions>("Kokishin.Bearer", options =>
                    options.ConfigurationManager = _authority.CreateConfigurationManager());
                services.PostConfigure<OpenIdConnectOptions>(OpenIdConnectDefaults.AuthenticationScheme, options =>
                    options.ConfigurationManager = _authority.CreateConfigurationManager());
                services.AddSingleton<IStartupFilter, AuthenticationProbeStartupFilter>();
            });
        }

        private void InitializeDatabase()
        {
            using var scope = Services.CreateScope();
            var dbContext = scope.ServiceProvider
                .GetRequiredService<Kokishin.Infrastructure.Persistence.KokishinDbContext>();
            dbContext.Database.Migrate();
            if (!dbContext.Accounts.Any(account => account.Id == ActiveAccountId))
            {
                var account = new Account(
                    TestOidcAuthority.Issuer,
                    "provider-subject",
                    AccountRole.Learner);
                account.Activate();
                dbContext.Accounts.Add(account);
                dbContext.SaveChanges();
            }
        }

        protected override void Dispose(bool disposing)
        {
            base.Dispose(disposing);
            if (disposing && _deleteDatabaseOnDispose)
            {
                File.Delete(_databasePath);
            }
        }
    }

    private sealed class AuthenticationProbeStartupFilter : IStartupFilter
    {
        public Action<IApplicationBuilder> Configure(Action<IApplicationBuilder> next) =>
            app =>
            {
                next(app);
                app.Use(async (context, nextMiddleware) =>
                {
                    var rolePolicy = context.Request.Path.Value switch
                    {
                        "/test/authorize/instructor" => AuthorizationPolicies.Instructor,
                        "/test/authorize/learner" => AuthorizationPolicies.Learner,
                        _ => null
                    };
                    if (rolePolicy is not null)
                    {
                        var authorization = context.RequestServices.GetRequiredService<IAuthorizationService>();
                        var authorizationResult =
                            await authorization.AuthorizeAsync(context.User, null, rolePolicy);
                        if (!authorizationResult.Succeeded)
                        {
                            if (context.User.Identity?.IsAuthenticated == true)
                            {
                                await context.ForbidAsync();
                            }
                            else
                            {
                                await context.ChallengeAsync();
                            }

                            return;
                        }

                        await context.Response.WriteAsync("authorized");
                        return;
                    }

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
                                        new Claim(ClaimTypes.NameIdentifier, ActiveAccountId.ToString("D")),
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
            string? nameIdentifier = null,
            string? role = null)
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

            if (role is not null)
            {
                claims.Add(new Claim(ClaimTypes.Role, role));
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
