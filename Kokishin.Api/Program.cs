using System.Diagnostics;
using System.Security.Claims;
using Kokishin.Application;
using Kokishin.Application.Courses;
using Kokishin.Application.Enrollments;
using Kokishin.Api;
using Kokishin.Application.Profiles;
using Kokishin.Infrastructure;
using Kokishin.Domain.Accounts;
using Kokishin.Domain.Courses;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.OpenIdConnect;
using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.OpenApi;

var builder = WebApplication.CreateBuilder(args);
builder.Services.AddApplication();
builder.Services.AddInfrastructure(builder.Configuration);
var authenticationSettings = AuthentikAuthenticationSettings.Read(builder.Configuration, builder.Environment);
var dataProtectionSettings = PersistentDataProtectionSettings.Read(
    builder.Configuration,
    builder.Environment,
    authenticationSettings is not null);
if (dataProtectionSettings is not null)
{
    builder.Services.AddDataProtection()
        .PersistKeysToFileSystem(dataProtectionSettings.KeyStorageDirectory)
        .ProtectKeysWithCertificate(dataProtectionSettings.KeyEncryptionCertificate)
        .SetApplicationName("Kokishin");
    builder.Services.AddSingleton(dataProtectionSettings.KeyEncryptionCertificate);
}
builder.Services.AddAuthentication();
builder.Services.AddAuthorization(options =>
{
    options.AddPolicy(AuthorizationPolicies.Instructor, policy =>
        policy.RequireAuthenticatedUser()
            .AddRequirements(new AccountRoleRequirement(AccountRole.Instructor)));
    options.AddPolicy(AuthorizationPolicies.Learner, policy =>
        policy.RequireAuthenticatedUser()
            .AddRequirements(new AccountRoleRequirement(AccountRole.Learner)));
});
builder.Services.AddScoped<IAuthorizationHandler, AccountRoleAuthorizationHandler>();
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
    app.UseCookieCsrfProtection();
}

if (authenticationSettings is not null)
{
    app.MapGet("/auth/login", (HttpContext context) =>
        Results.Challenge(
            new AuthenticationProperties { RedirectUri = "/" },
            [OpenIdConnectDefaults.AuthenticationScheme]))
        .AllowAnonymous()
        .ExcludeFromDescription();

    app.MapGet("/auth/csrf", (HttpContext context, IAntiforgery antiforgery) =>
        Results.Ok(new { requestToken = antiforgery.GetAndStoreTokens(context).RequestToken }))
        .RequireAuthorization()
        .ExcludeFromDescription();

    app.MapGet("/me/profile", GetOwnProfileAsync)
        .RequireAuthorization()
        .WithSummary("Get the authenticated user's profile")
        .Produces<ProfileResponse>()
        .ProducesProblem(StatusCodes.Status401Unauthorized)
        .ProducesProblem(StatusCodes.Status404NotFound);

    app.MapPut("/me/profile", UpdateOwnProfileAsync)
        .RequireAuthorization()
        .WithSummary("Create or update the authenticated user's profile")
        .Produces<ProfileResponse>()
        .ProducesValidationProblem()
        .ProducesProblem(StatusCodes.Status401Unauthorized);

    app.MapPost("/courses", CreateCourseAsync)
        .RequireAuthorization(AuthorizationPolicies.Instructor)
        .WithSummary("Create a course owned by the authenticated Instructor")
        .Produces<CourseResponse>(StatusCodes.Status201Created)
        .ProducesValidationProblem()
        .ProducesProblem(StatusCodes.Status401Unauthorized)
        .ProducesProblem(StatusCodes.Status403Forbidden);

    app.MapGet("/courses", ListOwnedCoursesAsync)
        .RequireAuthorization(AuthorizationPolicies.Instructor)
        .WithSummary("List courses owned by the authenticated Instructor")
        .Produces<CourseResponse[]>()
        .ProducesProblem(StatusCodes.Status401Unauthorized)
        .ProducesProblem(StatusCodes.Status403Forbidden);

    app.MapGet("/courses/{courseId:guid}", GetOwnedCourseAsync)
        .RequireAuthorization(AuthorizationPolicies.Instructor)
        .WithSummary("Get a course owned by the authenticated Instructor")
        .Produces<CourseResponse>()
        .ProducesProblem(StatusCodes.Status401Unauthorized)
        .ProducesProblem(StatusCodes.Status403Forbidden)
        .ProducesProblem(StatusCodes.Status404NotFound);

    app.MapPut("/courses/{courseId:guid}", UpdateOwnedCourseAsync)
        .RequireAuthorization(AuthorizationPolicies.Instructor)
        .WithSummary("Update information for a course owned by the authenticated Instructor")
        .Produces<CourseResponse>()
        .ProducesValidationProblem()
        .ProducesProblem(StatusCodes.Status401Unauthorized)
        .ProducesProblem(StatusCodes.Status403Forbidden)
        .ProducesProblem(StatusCodes.Status404NotFound);

    app.MapPut("/courses/{courseId:guid}/publication-status", ChangeCoursePublicationStatusAsync)
        .RequireAuthorization(AuthorizationPolicies.Instructor)
        .WithSummary("Change the publication status of a course owned by the authenticated Instructor")
        .Produces<CourseResponse>()
        .ProducesValidationProblem()
        .ProducesProblem(StatusCodes.Status401Unauthorized)
        .ProducesProblem(StatusCodes.Status403Forbidden)
        .ProducesProblem(StatusCodes.Status404NotFound);

    app.MapPost("/courses/{courseId:guid}/enrollments", EnrollLearnerAsync)
        .RequireAuthorization(AuthorizationPolicies.Instructor)
        .WithSummary("Enroll an active Learner in a course owned by the authenticated Instructor")
        .Produces<EnrollmentResponse>(StatusCodes.Status201Created)
        .Produces<EnrollmentResponse>(StatusCodes.Status200OK)
        .ProducesValidationProblem()
        .ProducesProblem(StatusCodes.Status401Unauthorized)
        .ProducesProblem(StatusCodes.Status403Forbidden)
        .ProducesProblem(StatusCodes.Status404NotFound)
        .ProducesProblem(StatusCodes.Status409Conflict);

    app.MapGet("/courses/{courseId:guid}/enrollments", ListCourseEnrollmentsAsync)
        .RequireAuthorization(AuthorizationPolicies.Instructor)
        .WithSummary("List enrollments for a course owned by the authenticated Instructor")
        .Produces<EnrollmentResponse[]>()
        .ProducesProblem(StatusCodes.Status401Unauthorized)
        .ProducesProblem(StatusCodes.Status403Forbidden)
        .ProducesProblem(StatusCodes.Status404NotFound);

    app.MapPost(
            "/courses/{courseId:guid}/enrollments/{learnerAccountId:guid}/suspend",
            (HttpContext context, Guid courseId, Guid learnerAccountId, IEnrollmentService service) =>
                ChangeOwnedEnrollmentAsync(
                    context,
                    courseId,
                    learnerAccountId,
                    EnrollmentAction.Suspend,
                    service))
        .RequireAuthorization(AuthorizationPolicies.Instructor)
        .WithSummary("Suspend a Learner's enrollment in a course owned by the authenticated Instructor")
        .Produces<EnrollmentResponse>()
        .ProducesProblem(StatusCodes.Status401Unauthorized)
        .ProducesProblem(StatusCodes.Status403Forbidden)
        .ProducesProblem(StatusCodes.Status404NotFound)
        .ProducesProblem(StatusCodes.Status409Conflict);

    app.MapPost(
            "/courses/{courseId:guid}/enrollments/{learnerAccountId:guid}/reactivate",
            (HttpContext context, Guid courseId, Guid learnerAccountId, IEnrollmentService service) =>
                ChangeOwnedEnrollmentAsync(
                    context,
                    courseId,
                    learnerAccountId,
                    EnrollmentAction.Reactivate,
                    service))
        .RequireAuthorization(AuthorizationPolicies.Instructor)
        .WithSummary("Reactivate a suspended enrollment in a course owned by the authenticated Instructor")
        .Produces<EnrollmentResponse>()
        .ProducesProblem(StatusCodes.Status401Unauthorized)
        .ProducesProblem(StatusCodes.Status403Forbidden)
        .ProducesProblem(StatusCodes.Status404NotFound)
        .ProducesProblem(StatusCodes.Status409Conflict);

    app.MapPost(
            "/courses/{courseId:guid}/enrollments/{learnerAccountId:guid}/remove",
            (HttpContext context, Guid courseId, Guid learnerAccountId, IEnrollmentService service) =>
                ChangeOwnedEnrollmentAsync(
                    context,
                    courseId,
                    learnerAccountId,
                    EnrollmentAction.Remove,
                    service))
        .RequireAuthorization(AuthorizationPolicies.Instructor)
        .WithSummary("Remove a Learner's enrollment from a course owned by the authenticated Instructor")
        .Produces<EnrollmentResponse>()
        .ProducesProblem(StatusCodes.Status401Unauthorized)
        .ProducesProblem(StatusCodes.Status403Forbidden)
        .ProducesProblem(StatusCodes.Status404NotFound)
        .ProducesProblem(StatusCodes.Status409Conflict);

    app.MapGet("/me/enrollments", ListOwnEnrollmentsAsync)
        .RequireAuthorization(AuthorizationPolicies.Learner)
        .WithSummary("List enrollments for the authenticated Learner")
        .Produces<EnrollmentResponse[]>()
        .ProducesProblem(StatusCodes.Status401Unauthorized)
        .ProducesProblem(StatusCodes.Status403Forbidden);

    app.MapGet("/me/enrollments/{courseId:guid}", GetOwnEnrollmentAsync)
        .RequireAuthorization(AuthorizationPolicies.Learner)
        .WithSummary("Get the authenticated Learner's enrollment in a course")
        .Produces<EnrollmentResponse>()
        .ProducesProblem(StatusCodes.Status401Unauthorized)
        .ProducesProblem(StatusCodes.Status403Forbidden)
        .ProducesProblem(StatusCodes.Status404NotFound);

    app.MapPost("/me/enrollments/{courseId:guid}/withdraw", WithdrawOwnEnrollmentAsync)
        .RequireAuthorization(AuthorizationPolicies.Learner)
        .WithSummary("Withdraw the authenticated Learner's enrollment in a course")
        .Produces<EnrollmentResponse>()
        .ProducesProblem(StatusCodes.Status401Unauthorized)
        .ProducesProblem(StatusCodes.Status403Forbidden)
        .ProducesProblem(StatusCodes.Status404NotFound)
        .ProducesProblem(StatusCodes.Status409Conflict);
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

static async Task<IResult> GetOwnProfileAsync(HttpContext context, IProfileService profileService)
{
    if (!TryGetAccountId(context.User, out var accountId))
    {
        return Results.Forbid();
    }

    var profile = await profileService.GetOwnProfileAsync(accountId, context.RequestAborted);
    return profile is null
        ? Results.NotFound()
        : Results.Ok(new ProfileResponse(profile.DisplayName, profile.Description));
}

static async Task<IResult> UpdateOwnProfileAsync(
    HttpContext context,
    UpdateProfileRequest request,
    IProfileService profileService)
{
    if (!TryGetAccountId(context.User, out var accountId))
    {
        return Results.Forbid();
    }

    try
    {
        var profile = await profileService.UpdateOwnProfileAsync(
            accountId,
            request.DisplayName,
            request.Description,
            context.RequestAborted);
        return Results.Ok(new ProfileResponse(profile.DisplayName, profile.Description));
    }
    catch (ArgumentException exception) when (exception.ParamName is "displayName" or "description")
    {
        var field = exception.ParamName!;
        return Results.ValidationProblem(new Dictionary<string, string[]>
        {
            [field] = [exception.Message]
        });
    }
}

static async Task<IResult> CreateCourseAsync(
    HttpContext context,
    CreateCourseRequest request,
    ICourseService courseService)
{
    if (!TryGetAccountId(context.User, out var ownerId))
    {
        return Results.Forbid();
    }

    try
    {
        var course = await courseService.CreateCourseAsync(
            ownerId,
            request.Title,
            request.Description,
            context.RequestAborted);
        var response = new CourseResponse(
            course.Id,
            course.OwnerId,
            course.Title,
            course.Description,
            course.Status.ToString());
        return Results.Created($"/courses/{course.Id:D}", response);
    }
    catch (ArgumentException exception) when (exception.ParamName is "title" or "description")
    {
        var field = exception.ParamName!;
        return Results.ValidationProblem(new Dictionary<string, string[]>
        {
            [field] = [exception.Message]
        });
    }
}

static async Task<IResult> ListOwnedCoursesAsync(HttpContext context, ICourseService courseService)
{
    if (!TryGetAccountId(context.User, out var ownerId))
    {
        return Results.Forbid();
    }

    var courses = await courseService.ListOwnedCoursesAsync(ownerId, context.RequestAborted);
    return Results.Ok(courses.Select(course => new CourseResponse(
        course.Id,
        course.OwnerId,
        course.Title,
        course.Description,
        course.Status.ToString())));
}

static async Task<IResult> GetOwnedCourseAsync(
    HttpContext context,
    Guid courseId,
    ICourseService courseService)
{
    if (!TryGetAccountId(context.User, out var ownerId))
    {
        return Results.Forbid();
    }

    var course = await courseService.GetOwnedCourseAsync(ownerId, courseId, context.RequestAborted);
    return course is null
        ? Results.NotFound()
        : Results.Ok(new CourseResponse(
            course.Id,
            course.OwnerId,
            course.Title,
            course.Description,
            course.Status.ToString()));
}

static async Task<IResult> UpdateOwnedCourseAsync(
    HttpContext context,
    Guid courseId,
    UpdateCourseRequest request,
    ICourseService courseService)
{
    if (!TryGetAccountId(context.User, out var ownerId))
    {
        return Results.Forbid();
    }

    try
    {
        var course = await courseService.UpdateOwnedCourseAsync(
            ownerId,
            courseId,
            request.Title,
            request.Description,
            context.RequestAborted);
        return course is null
            ? Results.NotFound()
            : Results.Ok(new CourseResponse(
                course.Id,
                course.OwnerId,
                course.Title,
                course.Description,
                course.Status.ToString()));
    }
    catch (ArgumentException exception) when (exception.ParamName is "title" or "description")
    {
        var field = exception.ParamName!;
        return Results.ValidationProblem(new Dictionary<string, string[]>
        {
            [field] = [exception.Message]
        });
    }
}

static async Task<IResult> ChangeCoursePublicationStatusAsync(
    HttpContext context,
    Guid courseId,
    ChangeCoursePublicationStatusRequest request,
    ICourseService courseService)
{
    if (!TryGetAccountId(context.User, out var ownerId))
    {
        return Results.Forbid();
    }

    var status = request.Status switch
    {
        nameof(CourseStatus.Draft) => CourseStatus.Draft,
        nameof(CourseStatus.Published) => CourseStatus.Published,
        _ => (CourseStatus?)null
    };
    if (status is null)
    {
        return PublicationStatusValidationProblem("Only Draft and Published are supported.");
    }

    try
    {
        var course = await courseService.ChangeOwnedCoursePublicationStatusAsync(
            ownerId,
            courseId,
            status.Value,
            context.RequestAborted);
        return course is null
            ? Results.NotFound()
            : Results.Ok(new CourseResponse(
                course.Id,
                course.OwnerId,
                course.Title,
                course.Description,
                course.Status.ToString()));
    }
    catch (Exception exception) when (exception is ArgumentException or InvalidOperationException)
    {
        return PublicationStatusValidationProblem(exception.Message);
    }
}

static IResult PublicationStatusValidationProblem(string message) =>
    Results.ValidationProblem(new Dictionary<string, string[]>
    {
        ["status"] = [message]
    });

static async Task<IResult> EnrollLearnerAsync(
    HttpContext context,
    Guid courseId,
    EnrollLearnerRequest request,
    IEnrollmentService enrollmentService)
{
    if (!TryGetAccountId(context.User, out var instructorId))
    {
        return Results.Forbid();
    }

    if (request.LearnerAccountId == Guid.Empty)
    {
        return Results.ValidationProblem(new Dictionary<string, string[]>
        {
            ["learnerAccountId"] = ["Learner account is required."]
        });
    }

    var result = await enrollmentService.EnrollLearnerAsync(
        instructorId,
        courseId,
        request.LearnerAccountId,
        context.RequestAborted);
    if (result.Enrollment is not { } enrollment)
    {
        return result.Outcome switch
        {
            EnrollLearnerOutcome.InstructorNotAuthorized or EnrollLearnerOutcome.InstructorInactive =>
                Results.Forbid(),
            EnrollLearnerOutcome.CourseNotFound or EnrollLearnerOutcome.LearnerNotFound =>
                Results.NotFound(),
            EnrollLearnerOutcome.LearnerNotEligible => Results.ValidationProblem(
                new Dictionary<string, string[]>
                {
                    ["learnerAccountId"] = ["Only active Learner accounts may be enrolled."]
                }),
            _ => throw new InvalidOperationException("The enrollment result is inconsistent.")
        };
    }

    var response = new EnrollmentResponse(
        enrollment.CourseId,
        enrollment.LearnerAccountId,
        enrollment.Status.ToString());
    return result.Outcome switch
    {
        EnrollLearnerOutcome.Created =>
            Results.Created($"/courses/{courseId:D}/enrollments", response),
        EnrollLearnerOutcome.AlreadyActive or EnrollLearnerOutcome.ReEnrolled =>
            Results.Ok(response),
        EnrollLearnerOutcome.EnrollmentSuspended => Results.Problem(
            statusCode: StatusCodes.Status409Conflict,
            title: "Enrollment is suspended.",
            detail: "The suspended enrollment must be reactivated before the learner can be enrolled again."),
        _ => throw new InvalidOperationException("The enrollment result is inconsistent.")
    };
}

static async Task<IResult> ListCourseEnrollmentsAsync(
    HttpContext context,
    Guid courseId,
    IEnrollmentService enrollmentService)
{
    if (!TryGetAccountId(context.User, out var instructorId))
    {
        return Results.Forbid();
    }

    var enrollments = await enrollmentService.ListForOwnedCourseAsync(
        instructorId,
        courseId,
        context.RequestAborted);
    return enrollments is null
        ? Results.NotFound()
        : Results.Ok(enrollments.Select(ToEnrollmentResponse));
}

static async Task<IResult> ChangeOwnedEnrollmentAsync(
    HttpContext context,
    Guid courseId,
    Guid learnerAccountId,
    EnrollmentAction action,
    IEnrollmentService enrollmentService)
{
    if (!TryGetAccountId(context.User, out var instructorId))
    {
        return Results.Forbid();
    }

    var result = await enrollmentService.ChangeOwnedEnrollmentAsync(
        instructorId,
        courseId,
        learnerAccountId,
        action,
        context.RequestAborted);
    return EnrollmentChangeResponse(result);
}

static async Task<IResult> ListOwnEnrollmentsAsync(
    HttpContext context,
    IEnrollmentService enrollmentService)
{
    if (!TryGetAccountId(context.User, out var learnerAccountId))
    {
        return Results.Forbid();
    }

    var enrollments = await enrollmentService.ListForLearnerAsync(
        learnerAccountId,
        context.RequestAborted);
    return Results.Ok(enrollments.Select(ToEnrollmentResponse));
}

static async Task<IResult> GetOwnEnrollmentAsync(
    HttpContext context,
    Guid courseId,
    IEnrollmentService enrollmentService)
{
    if (!TryGetAccountId(context.User, out var learnerAccountId))
    {
        return Results.Forbid();
    }

    var enrollment = await enrollmentService.GetForLearnerAsync(
        learnerAccountId,
        courseId,
        context.RequestAborted);
    return enrollment is null
        ? Results.NotFound()
        : Results.Ok(ToEnrollmentResponse(enrollment));
}

static async Task<IResult> WithdrawOwnEnrollmentAsync(
    HttpContext context,
    Guid courseId,
    IEnrollmentService enrollmentService)
{
    if (!TryGetAccountId(context.User, out var learnerAccountId))
    {
        return Results.Forbid();
    }

    var result = await enrollmentService.WithdrawOwnEnrollmentAsync(
        learnerAccountId,
        courseId,
        context.RequestAborted);
    return EnrollmentChangeResponse(result);
}

static IResult EnrollmentChangeResponse(EnrollmentChangeResult result)
{
    if (result.Enrollment is not { } enrollment)
    {
        return result.Outcome switch
        {
            EnrollmentChangeOutcome.InstructorNotAuthorized or
            EnrollmentChangeOutcome.InstructorInactive or
            EnrollmentChangeOutcome.LearnerNotAuthorized or
            EnrollmentChangeOutcome.LearnerInactive => Results.Forbid(),
            EnrollmentChangeOutcome.CourseNotFound or EnrollmentChangeOutcome.EnrollmentNotFound =>
                Results.NotFound(),
            _ => throw new InvalidOperationException("The enrollment change result is inconsistent.")
        };
    }

    return result.Outcome switch
    {
        EnrollmentChangeOutcome.Changed => Results.Ok(ToEnrollmentResponse(enrollment)),
        EnrollmentChangeOutcome.InvalidTransition => Results.Problem(
            statusCode: StatusCodes.Status409Conflict,
            title: "Enrollment state change is not allowed.",
            detail: "The requested action is not permitted for the current enrollment state."),
        _ => throw new InvalidOperationException("The enrollment change result is inconsistent.")
    };
}

static EnrollmentResponse ToEnrollmentResponse(Kokishin.Domain.Enrollments.Enrollment enrollment) =>
    new(enrollment.CourseId, enrollment.LearnerAccountId, enrollment.Status.ToString());

static bool TryGetAccountId(ClaimsPrincipal principal, out Guid accountId) =>
    Guid.TryParse(principal.FindFirstValue(ClaimTypes.NameIdentifier), out accountId);

internal sealed record UpdateProfileRequest(string DisplayName, string? Description);

internal sealed record ProfileResponse(string DisplayName, string? Description);

internal sealed record CreateCourseRequest(string Title, string? Description);

internal sealed record UpdateCourseRequest(string Title, string? Description);

internal sealed record ChangeCoursePublicationStatusRequest(string? Status);

internal sealed record EnrollLearnerRequest(Guid LearnerAccountId);

internal sealed record CourseResponse(
    Guid Id,
    Guid OwnerId,
    string Title,
    string? Description,
    string Status);

internal sealed record EnrollmentResponse(
    Guid CourseId,
    Guid LearnerAccountId,
    string Status);

public partial class Program;
