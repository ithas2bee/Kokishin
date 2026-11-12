using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using Kokishin.Application.Accounts;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authentication.OpenIdConnect;
using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Kokishin.Infrastructure.Persistence.Sessions;
using Microsoft.IdentityModel.Tokens;

namespace Kokishin.Api;

internal static class AuthentikAuthenticationExtensions
{
    private const string DefaultScheme = "Kokishin";
    private const string CookieScheme = "Kokishin.Cookie";
    private const string BearerScheme = "Kokishin.Bearer";
    private const string SessionCookieName = "__Host-Kokishin.Session";
    private const string SessionStartedAt = "kokishin:session_started_at";

    private static readonly Guid UserIdNamespace = Guid.Parse("47fbe87b-685f-49d4-a78b-4533d1bf164d");

    public static IServiceCollection AddAuthentikAuthentication(
        this IServiceCollection services,
        AuthentikAuthenticationSettings settings)
    {
        services.AddAntiforgery(options =>
        {
            options.HeaderName = "X-XSRF-TOKEN";
            options.Cookie.Name = "__Host-Kokishin.Antiforgery";
            options.Cookie.Path = "/";
            options.Cookie.SecurePolicy = CookieSecurePolicy.Always;
            options.Cookie.SameSite = SameSiteMode.Strict;
        });
        services.AddSingleton<ITicketStore, ProtectedSessionTicketStore>();
        services.AddAuthentication(options =>
            {
                options.DefaultScheme = DefaultScheme;
                options.DefaultAuthenticateScheme = DefaultScheme;
                options.DefaultChallengeScheme = DefaultScheme;
            })
            .AddPolicyScheme(DefaultScheme, "Kokishin cookie or bearer authentication", options =>
            {
                options.ForwardDefaultSelector = context =>
                    context.Request.Headers.Authorization.ToString()
                        .StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase)
                        ? BearerScheme
                        : CookieScheme;
            })
            .AddCookie(CookieScheme, options =>
            {
                options.Cookie.Name = SessionCookieName;
                options.Cookie.Path = "/";
                options.Cookie.HttpOnly = true;
                options.Cookie.SecurePolicy = CookieSecurePolicy.Always;
                options.Cookie.SameSite = SameSiteMode.Lax;
                options.ExpireTimeSpan = TimeSpan.FromMinutes(30);
                options.SlidingExpiration = true;
                options.Events = new CookieAuthenticationEvents
                {
                    OnRedirectToLogin = context =>
                    {
                        context.Response.StatusCode = StatusCodes.Status401Unauthorized;
                        return Task.CompletedTask;
                    },
                    OnRedirectToAccessDenied = context =>
                    {
                        context.Response.StatusCode = StatusCodes.Status403Forbidden;
                        return Task.CompletedTask;
                    },
                    OnValidatePrincipal = async context =>
                    {
                        if (!context.Properties.Items.TryGetValue(SessionStartedAt, out var startedAtValue) ||
                            !long.TryParse(
                                startedAtValue,
                                out var startedAt) ||
                            DateTimeOffset.UtcNow - DateTimeOffset.FromUnixTimeSeconds(startedAt) >
                            TimeSpan.FromDays(7))
                        {
                            context.RejectPrincipal();
                            return;
                        }

                        var userId = context.Principal?.FindFirst(ClaimTypes.NameIdentifier)?.Value;
                        if (!Guid.TryParse(userId, out var accountId) ||
                            !await context.HttpContext.RequestServices
                                .GetRequiredService<IAccountLifecycleService>()
                                .IsActiveAsync(accountId, context.HttpContext.RequestAborted))
                        {
                            context.RejectPrincipal();
                        }
                    }
                };
            })
            .AddOpenIdConnect(OpenIdConnectDefaults.AuthenticationScheme, options =>
            {
                options.Authority = settings.Authority.AbsoluteUri;
                options.ClientId = settings.ClientId;
                options.ClientSecret = settings.ClientSecret;
                options.SignInScheme = CookieScheme;
                options.CallbackPath = new PathString(settings.CallbackUrl.AbsolutePath);
                options.ResponseType = "code";
                options.UsePkce = true;
                options.SaveTokens = true;
                options.MapInboundClaims = false;
                options.RequireHttpsMetadata = true;
                options.TokenValidationParameters = CreateTokenValidationParameters(
                    settings.Authority.AbsoluteUri,
                    settings.ClientId);
                options.CorrelationCookie.HttpOnly = true;
                options.CorrelationCookie.SecurePolicy = CookieSecurePolicy.Always;
                options.NonceCookie.HttpOnly = true;
                options.NonceCookie.SecurePolicy = CookieSecurePolicy.Always;
                options.Events = new OpenIdConnectEvents
                {
                    OnRedirectToIdentityProvider = context =>
                    {
                        context.ProtocolMessage.RedirectUri = settings.CallbackUrl.AbsoluteUri;
                        return Task.CompletedTask;
                    },
                    OnTokenValidated = context =>
                    {
                        var properties = context.Properties;
                        if (properties is null)
                        {
                            properties = new AuthenticationProperties();
                            context.Properties = properties;
                        }

                        properties.Items[SessionStartedAt] =
                            DateTimeOffset.UtcNow.ToUnixTimeSeconds().ToString(
                                System.Globalization.CultureInfo.InvariantCulture);
                        return MapInternalIdentity(
                            context.Principal,
                            context.SecurityToken?.Issuer,
                            context.Fail,
                            context.HttpContext);
                    },
                    OnRemoteFailure = async context =>
                    {
                        context.HandleResponse();
                        context.Response.StatusCode = StatusCodes.Status401Unauthorized;
                        await context.HttpContext.RequestServices
                            .GetRequiredService<IProblemDetailsService>()
                            .WriteAsync(new ProblemDetailsContext
                            {
                                HttpContext = context.HttpContext,
                                ProblemDetails = new ProblemDetails
                                {
                                    Status = StatusCodes.Status401Unauthorized,
                                    Title = "Authentication failed."
                                }
                            });
                    }
                };
            })
            .AddJwtBearer(BearerScheme, options =>
            {
                options.Authority = settings.Authority.AbsoluteUri;
                options.Audience = settings.Audience;
                options.MapInboundClaims = false;
                options.RequireHttpsMetadata = true;
                options.TokenValidationParameters = CreateTokenValidationParameters(
                    settings.Authority.AbsoluteUri,
                    settings.Audience);
                options.Events = new JwtBearerEvents
                {
                    OnTokenValidated = context => MapInternalIdentity(
                        context.Principal,
                        context.SecurityToken?.Issuer,
                        context.Fail,
                        context.HttpContext)
                };
            });

        services.AddOptions<CookieAuthenticationOptions>(CookieScheme)
            .Configure<ITicketStore>((options, ticketStore) => options.SessionStore = ticketStore);
        services.AddAuthorization();
        return services;
    }

    public static IApplicationBuilder UseCookieCsrfProtection(this IApplicationBuilder app) =>
        app.Use(async (context, next) =>
        {
            var method = context.Request.Method;
            var isStateChanging = method is not "GET" and not "HEAD" and not "OPTIONS" and not "TRACE";
            var hasBearerToken = context.Request.Headers.Authorization.ToString()
                .StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase);
            if (isStateChanging &&
                !hasBearerToken &&
                context.Request.Cookies.ContainsKey(SessionCookieName) &&
                !await context.RequestServices.GetRequiredService<IAntiforgery>().IsRequestValidAsync(context))
            {
                context.Response.StatusCode = StatusCodes.Status400BadRequest;
                await context.RequestServices.GetRequiredService<IProblemDetailsService>()
                    .WriteAsync(new ProblemDetailsContext
                    {
                        HttpContext = context,
                        ProblemDetails = new ProblemDetails
                        {
                            Status = StatusCodes.Status400BadRequest,
                            Title = "The antiforgery token is missing or invalid."
                        }
                    });
                return;
            }

            await next();
        });

    private static TokenValidationParameters CreateTokenValidationParameters(string issuer, string audience) =>
        new()
        {
            ValidateIssuer = true,
            ValidIssuer = issuer,
            ValidateAudience = true,
            ValidAudience = audience,
            ValidateLifetime = true,
            ValidateIssuerSigningKey = true,
            RequireSignedTokens = true,
            ClockSkew = TimeSpan.FromMinutes(1),
            NameClaimType = ClaimTypes.NameIdentifier
        };

    private static Task MapInternalIdentity(
        ClaimsPrincipal? principal,
        string? issuer,
        Action<string> fail,
        HttpContext httpContext)
    {
        var subject = principal?.FindFirst("sub")?.Value;
        if (string.IsNullOrWhiteSpace(issuer) ||
            string.IsNullOrWhiteSpace(subject) ||
            principal?.Identity is not ClaimsIdentity identity)
        {
            fail("The validated identity is missing its issuer or subject.");
            return Task.CompletedTask;
        }

        foreach (var claim in identity.FindAll(ClaimTypes.NameIdentifier).ToArray())
        {
            identity.RemoveClaim(claim);
        }

        var userId = GetInternalUserId(issuer, subject);
        return MapInternalIdentityAsync(identity, userId, fail, httpContext);
    }

    private static async Task MapInternalIdentityAsync(
        ClaimsIdentity identity,
        Guid userId,
        Action<string> fail,
        HttpContext httpContext)
    {
        if (!await httpContext.RequestServices
                .GetRequiredService<IAccountLifecycleService>()
                .IsActiveAsync(userId, httpContext.RequestAborted))
        {
            fail("The Kokishin account is not active.");
            return;
        }

        identity.AddClaim(new Claim(
            ClaimTypes.NameIdentifier,
            userId.ToString("D"),
            ClaimValueTypes.String,
            "Kokishin"));
    }

    private static Guid GetInternalUserId(string issuer, string subject)
    {
        var namespaceBytes = UserIdNamespace.ToByteArray(bigEndian: true);
        var identityBytes = Encoding.UTF8.GetBytes($"{issuer}\0{subject}");
        var input = new byte[namespaceBytes.Length + identityBytes.Length];
        namespaceBytes.CopyTo(input, 0);
        identityBytes.CopyTo(input, namespaceBytes.Length);
        var hash = SHA256.HashData(input);
        hash[6] = (byte)((hash[6] & 0x0F) | 0x80);
        hash[8] = (byte)((hash[8] & 0x3F) | 0x80);
        return new Guid(hash.AsSpan(0, 16), bigEndian: true);
    }
}
