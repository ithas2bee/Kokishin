using System.IdentityModel.Tokens.Jwt;
using System.Net;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text.Json;
using System.Net.Http.Json;
using Kokishin.Application.Accounts;
using Kokishin.Application.Courses;
using Kokishin.Application.Enrollments;
using Kokishin.Domain.Courses;
using Kokishin.Api;
using Kokishin.Domain.Accounts;
using Kokishin.Domain.Enrollments;
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
        var token = TestIdentities.Learner.CreateToken(
            authority,
            nameIdentifier: "provider-supplied-id")!;

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
        using var request = CreateIdentityRequest(authority, TestIdentities.Anonymous);
        request.Headers.Add("X-User-Id", "client-controlled-user");

        using var response = await client.SendAsync(request);
        var body = await response.Content.ReadAsStringAsync();

        AssertSafeAuthenticationFailure(response, body);
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

    [Theory]
    [InlineData(AccountRole.Instructor, "/test/authorize/instructor")]
    [InlineData(AccountRole.Learner, "/test/authorize/learner")]
    public async Task RolePolicy_UnauthenticatedCallerGetsUnauthorized(AccountRole role, string path)
    {
        using var authority = new TestOidcAuthority();
        using var factory = CreateFactory(authority);
        using var client = factory.CreateClient();
        using var request = new HttpRequestMessage(
            HttpMethod.Post,
            $"{path}?role={role}")
        {
            Content = JsonContent.Create(new { role = role.ToString() })
        };
        request.Headers.Add("X-Role", role.ToString());

        using var response = await client.SendAsync(request);
        using var problem = JsonDocument.Parse(await response.Content.ReadAsStringAsync());

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
        Assert.Equal((int)HttpStatusCode.Unauthorized, problem.RootElement.GetProperty("status").GetInt32());
    }

    [Theory]
    [InlineData(AccountRole.Learner, AccountRole.Instructor, "/test/authorize/instructor")]
    [InlineData(AccountRole.Instructor, AccountRole.Learner, "/test/authorize/learner")]
    public async Task RolePolicy_WrongRoleCannotGainAccessFromClientSuppliedValues(
        AccountRole callerRole,
        AccountRole requiredRole,
        string path)
    {
        using var authority = new TestOidcAuthority();
        using var factory = CreateFactory(authority);
        using var client = factory.CreateClient();
        var identity = TestIdentities.For(callerRole);
        using var request = new HttpRequestMessage(
            HttpMethod.Post,
            $"{path}?role={requiredRole}")
        {
            Content = JsonContent.Create(new { role = requiredRole.ToString() })
        };
        request.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue(
            "Bearer",
            authority.CreateToken(identity.Subject, role: requiredRole.ToString()));
        request.Headers.Add("X-Role", requiredRole.ToString());

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
        var identity = TestIdentities.For(role);
        var path = role == AccountRole.Instructor
            ? "/test/authorize/instructor"
            : "/test/authorize/learner";

        using var response = await SendRoleAuthorizationRequestAsync(
            client,
            path,
            identity.CreateToken(authority)!);

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

    [Fact]
    public async Task CourseCreation_InstructorPersistsCourseUnderValidatedPrincipal()
    {
        using var authority = new TestOidcAuthority();
        using var factory = CreateFactory(authority);
        using var client = factory.CreateClient();
        var attackerOwnerId = Guid.NewGuid();

        using var response = await SendCourseRequestAsync(
            client,
            TestIdentities.Instructor.CreateToken(authority)!,
            new
            {
                title = "  Biology  ",
                description = "  An introduction.  ",
                ownerId = attackerOwnerId
            });
        using var result = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var courseId = Guid.Parse(result.RootElement.GetProperty("id").GetString()!);
        using var databaseScope = factory.Services.CreateScope();
        var database = databaseScope.ServiceProvider
            .GetRequiredService<Kokishin.Infrastructure.Persistence.KokishinDbContext>();
        var persisted = await database.Courses.SingleAsync(course => course.Id == courseId);

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        Assert.Equal($"/courses/{courseId:D}", response.Headers.Location?.OriginalString);
        Assert.Equal(TestIdentities.Instructor.AccountId, persisted.OwnerId);
        Assert.NotEqual(attackerOwnerId, persisted.OwnerId);
        Assert.Equal(TestIdentities.Instructor.AccountId,
            Guid.Parse(result.RootElement.GetProperty("ownerId").GetString()!));
        Assert.Equal("Biology", result.RootElement.GetProperty("title").GetString());
        Assert.Equal("An introduction.", result.RootElement.GetProperty("description").GetString());
        Assert.Equal(CourseStatus.Draft.ToString(), result.RootElement.GetProperty("status").GetString());
    }

    [Fact]
    public async Task CourseCreation_LearnerIsForbidden()
    {
        using var authority = new TestOidcAuthority();
        using var factory = CreateFactory(authority);
        using var client = factory.CreateClient();

        using var response = await SendCourseRequestAsync(
            client,
            TestIdentities.Learner.CreateToken(authority)!,
            new { title = "Biology" });

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task CourseCreation_AnonymousCallerIsUnauthorized()
    {
        using var authority = new TestOidcAuthority();
        using var factory = CreateFactory(authority);
        using var client = factory.CreateClient();

        using var response = await client.PostAsJsonAsync("/courses", new { title = "Biology" });

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task CourseEnrollment_InstructorEnrollsLearner_UsesAuthenticatedOwnerAndPreservesRole()
    {
        using var authority = new TestOidcAuthority();
        using var factory = CreateFactory(authority);
        using var client = factory.CreateClient();
        var course = await factory.Services.GetRequiredService<ICourseService>()
            .CreateCourseAsync(TestIdentities.Instructor.AccountId, "Biology");

        using var response = await SendEnrollmentRequestAsync(
            client,
            course.Id,
            TestIdentities.Instructor.CreateToken(authority)!,
            new
            {
                learnerAccountId = TestIdentities.Learner.AccountId,
                role = nameof(AccountRole.Instructor),
                instructorId = Guid.NewGuid()
            });
        using var result = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var enrollment = result.RootElement;
        using var databaseScope = factory.Services.CreateScope();
        var database = databaseScope.ServiceProvider
            .GetRequiredService<Kokishin.Infrastructure.Persistence.KokishinDbContext>();
        var persisted = await database.Enrollments.SingleAsync();
        var persistedCourse = await database.Courses.SingleAsync(item => item.Id == course.Id);
        var persistedLearner = await database.Accounts
            .SingleAsync(account => account.Id == TestIdentities.Learner.AccountId);

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        Assert.Equal(course.Id, Guid.Parse(enrollment.GetProperty("courseId").GetString()!));
        Assert.Equal(
            TestIdentities.Learner.AccountId,
            Guid.Parse(enrollment.GetProperty("learnerAccountId").GetString()!));
        Assert.Equal(EnrollmentStatus.Active.ToString(), enrollment.GetProperty("status").GetString());
        Assert.Equal(TestIdentities.Instructor.AccountId, course.OwnerId);
        Assert.Equal(course.Id, persisted.CourseId);
        Assert.Equal(TestIdentities.Learner.AccountId, persisted.LearnerAccountId);
        Assert.Equal(EnrollmentStatus.Active, persisted.Status);
        Assert.Equal(TestIdentities.Instructor.AccountId, persistedCourse.OwnerId);
        Assert.NotEqual(TestIdentities.Learner.AccountId, persistedCourse.OwnerId);
        Assert.Equal(AccountRole.Learner, persistedLearner.Role);
    }

    [Fact]
    public async Task CourseEnrollment_RepeatedActiveEnrollment_IsIdempotent()
    {
        using var authority = new TestOidcAuthority();
        using var factory = CreateFactory(authority);
        using var client = factory.CreateClient();
        var course = await factory.Services.GetRequiredService<ICourseService>()
            .CreateCourseAsync(TestIdentities.Instructor.AccountId, "Biology");
        var body = new { learnerAccountId = TestIdentities.Learner.AccountId };

        using var firstResponse = await SendEnrollmentRequestAsync(
            client,
            course.Id,
            TestIdentities.Instructor.CreateToken(authority)!,
            body);
        using var secondResponse = await SendEnrollmentRequestAsync(
            client,
            course.Id,
            TestIdentities.Instructor.CreateToken(authority)!,
            body);
        using var databaseScope = factory.Services.CreateScope();
        var database = databaseScope.ServiceProvider
            .GetRequiredService<Kokishin.Infrastructure.Persistence.KokishinDbContext>();

        Assert.Equal(HttpStatusCode.Created, firstResponse.StatusCode);
        Assert.Equal(HttpStatusCode.OK, secondResponse.StatusCode);
        Assert.Equal(1, await database.Enrollments.CountAsync());
    }

    [Fact]
    public async Task CourseEnrollment_OnlyActiveOwnerInstructorMayEnroll()
    {
        using var authority = new TestOidcAuthority();
        using var factory = CreateFactory(authority);
        using var client = factory.CreateClient();
        var course = await factory.Services.GetRequiredService<ICourseService>()
            .CreateCourseAsync(TestIdentities.Instructor.AccountId, "Biology");
        var lifecycle = factory.Services.GetRequiredService<IAccountLifecycleService>();
        var otherInstructor = await lifecycle.ProvisionVerifiedIdentityAsync(
            TestOidcAuthority.Issuer,
            "other-enrollment-instructor",
            AccountRole.Instructor);
        await lifecycle.ActivateAsync(otherInstructor.Account.Id);

        using var anonymousResponse = await SendEnrollmentRequestAsync(
            client,
            course.Id,
            null,
            new { learnerAccountId = TestIdentities.Learner.AccountId });
        using var learnerResponse = await SendEnrollmentRequestAsync(
            client,
            course.Id,
            TestIdentities.Learner.CreateToken(authority)!,
            new { learnerAccountId = TestIdentities.Learner.AccountId });
        using var otherInstructorResponse = await SendEnrollmentRequestAsync(
            client,
            course.Id,
            authority.CreateToken("other-enrollment-instructor"),
            new { learnerAccountId = TestIdentities.Learner.AccountId });

        Assert.Equal(HttpStatusCode.Unauthorized, anonymousResponse.StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, learnerResponse.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, otherInstructorResponse.StatusCode);
    }

    [Fact]
    public async Task CourseEnrollment_UnknownCourseOrLearner_ReturnsNotFound()
    {
        using var authority = new TestOidcAuthority();
        using var factory = CreateFactory(authority);
        using var client = factory.CreateClient();
        var course = await factory.Services.GetRequiredService<ICourseService>()
            .CreateCourseAsync(TestIdentities.Instructor.AccountId, "Biology");
        var token = TestIdentities.Instructor.CreateToken(authority)!;

        using var unknownCourse = await SendEnrollmentRequestAsync(
            client,
            Guid.NewGuid(),
            token,
            new { learnerAccountId = TestIdentities.Learner.AccountId });
        using var unknownLearner = await SendEnrollmentRequestAsync(
            client,
            course.Id,
            token,
            new { learnerAccountId = Guid.NewGuid() });

        Assert.Equal(HttpStatusCode.NotFound, unknownCourse.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, unknownLearner.StatusCode);
    }

    [Fact]
    public async Task CourseEnrollment_InstructorOrInactiveAccountCannotBeEnrolled()
    {
        using var authority = new TestOidcAuthority();
        using var factory = CreateFactory(authority);
        using var client = factory.CreateClient();
        var course = await factory.Services.GetRequiredService<ICourseService>()
            .CreateCourseAsync(TestIdentities.Instructor.AccountId, "Biology");
        var lifecycle = factory.Services.GetRequiredService<IAccountLifecycleService>();
        var instructor = await lifecycle.ProvisionVerifiedIdentityAsync(
            TestOidcAuthority.Issuer,
            "learner-target-instructor",
            AccountRole.Instructor);
        await lifecycle.ActivateAsync(instructor.Account.Id);

        using var instructorResponse = await SendEnrollmentRequestAsync(
            client,
            course.Id,
            TestIdentities.Instructor.CreateToken(authority)!,
            new { learnerAccountId = instructor.Account.Id });
        await lifecycle.DeactivateAsync(TestIdentities.Learner.AccountId);
        using var inactiveLearnerResponse = await SendEnrollmentRequestAsync(
            client,
            course.Id,
            TestIdentities.Instructor.CreateToken(authority)!,
            new { learnerAccountId = TestIdentities.Learner.AccountId });
        using var databaseScope = factory.Services.CreateScope();
        var database = databaseScope.ServiceProvider
            .GetRequiredService<Kokishin.Infrastructure.Persistence.KokishinDbContext>();

        Assert.Equal(HttpStatusCode.BadRequest, instructorResponse.StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, inactiveLearnerResponse.StatusCode);
        Assert.Empty(await database.Enrollments.ToListAsync());
        Assert.Equal(
            AccountRole.Instructor,
            (await database.Accounts.SingleAsync(account => account.Id == instructor.Account.Id)).Role);
    }

    [Fact]
    public async Task CourseEnrollment_WithdrawnEnrollmentRequiresExplicitReEnrollment()
    {
        using var authority = new TestOidcAuthority();
        using var factory = CreateFactory(authority);
        using var client = factory.CreateClient();
        var course = await factory.Services.GetRequiredService<ICourseService>()
            .CreateCourseAsync(TestIdentities.Instructor.AccountId, "Biology");
        var enrollmentService = factory.Services.GetRequiredService<IEnrollmentService>();
        await enrollmentService.EnrollLearnerAsync(
            TestIdentities.Instructor.AccountId,
            course.Id,
            TestIdentities.Learner.AccountId);
        using (var databaseScope = factory.Services.CreateScope())
        {
            var database = databaseScope.ServiceProvider
                .GetRequiredService<Kokishin.Infrastructure.Persistence.KokishinDbContext>();
            var enrollment = await database.Enrollments.SingleAsync();
            enrollment.Withdraw();
            await database.SaveChangesAsync();
        }

        using var response = await SendEnrollmentRequestAsync(
            client,
            course.Id,
            TestIdentities.Instructor.CreateToken(authority)!,
            new { learnerAccountId = TestIdentities.Learner.AccountId });
        using var verifyScope = factory.Services.CreateScope();
        var verifyDatabase = verifyScope.ServiceProvider
            .GetRequiredService<Kokishin.Infrastructure.Persistence.KokishinDbContext>();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(EnrollmentStatus.Active,
            (await verifyDatabase.Enrollments.SingleAsync()).Status);
    }

    [Fact]
    public async Task CourseEnrollment_SuspendedEnrollmentCannotBeReactivatedByEnrollRequest()
    {
        using var authority = new TestOidcAuthority();
        using var factory = CreateFactory(authority);
        using var client = factory.CreateClient();
        var course = await factory.Services.GetRequiredService<ICourseService>()
            .CreateCourseAsync(TestIdentities.Instructor.AccountId, "Biology");
        await factory.Services.GetRequiredService<IEnrollmentService>().EnrollLearnerAsync(
            TestIdentities.Instructor.AccountId,
            course.Id,
            TestIdentities.Learner.AccountId);
        using (var databaseScope = factory.Services.CreateScope())
        {
            var database = databaseScope.ServiceProvider
                .GetRequiredService<Kokishin.Infrastructure.Persistence.KokishinDbContext>();
            var enrollment = await database.Enrollments.SingleAsync();
            enrollment.Suspend();
            await database.SaveChangesAsync();
        }

        using var response = await SendEnrollmentRequestAsync(
            client,
            course.Id,
            TestIdentities.Instructor.CreateToken(authority)!,
            new { learnerAccountId = TestIdentities.Learner.AccountId });
        using var verifyScope = factory.Services.CreateScope();
        var verifyDatabase = verifyScope.ServiceProvider
            .GetRequiredService<Kokishin.Infrastructure.Persistence.KokishinDbContext>();

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.Equal(EnrollmentStatus.Suspended,
            (await verifyDatabase.Enrollments.SingleAsync()).Status);
    }

    [Fact]
    public async Task CourseEnrollments_InstructorListsOnlyEnrollmentsForOwnedCourse()
    {
        using var authority = new TestOidcAuthority();
        using var factory = CreateFactory(authority);
        using var client = factory.CreateClient();
        var lifecycle = factory.Services.GetRequiredService<IAccountLifecycleService>();
        var otherInstructor = await lifecycle.ProvisionVerifiedIdentityAsync(
            TestOidcAuthority.Issuer,
            "other-listing-instructor",
            AccountRole.Instructor);
        await lifecycle.ActivateAsync(otherInstructor.Account.Id);
        var courseService = factory.Services.GetRequiredService<ICourseService>();
        var ownedCourse = await courseService.CreateCourseAsync(
            TestIdentities.Instructor.AccountId,
            "Owned course");
        var privateCourse = await courseService.CreateCourseAsync(
            otherInstructor.Account.Id,
            "Private course");
        var enrollmentService = factory.Services.GetRequiredService<IEnrollmentService>();
        await enrollmentService.EnrollLearnerAsync(
            TestIdentities.Instructor.AccountId,
            ownedCourse.Id,
            TestIdentities.Learner.AccountId);
        await enrollmentService.EnrollLearnerAsync(
            otherInstructor.Account.Id,
            privateCourse.Id,
            TestIdentities.Learner.AccountId);

        using var ownedResponse = await SendEnrollmentManagementRequestAsync(
            client,
            HttpMethod.Get,
            $"/courses/{ownedCourse.Id:D}/enrollments?instructorId={otherInstructor.Account.Id:D}",
            TestIdentities.Instructor.CreateToken(authority)!);
        using var ownedResult = JsonDocument.Parse(await ownedResponse.Content.ReadAsStringAsync());
        using var privateResponse = await SendEnrollmentManagementRequestAsync(
            client,
            HttpMethod.Get,
            $"/courses/{privateCourse.Id:D}/enrollments",
            TestIdentities.Instructor.CreateToken(authority)!);
        using var privateMutationResponse = await SendEnrollmentManagementRequestAsync(
            client,
            HttpMethod.Post,
            $"/courses/{privateCourse.Id:D}/enrollments/{TestIdentities.Learner.AccountId:D}/suspend",
            TestIdentities.Instructor.CreateToken(authority)!);

        Assert.Equal(HttpStatusCode.OK, ownedResponse.StatusCode);
        Assert.Equal(1, ownedResult.RootElement.GetArrayLength());
        Assert.Equal(ownedCourse.Id.ToString("D"),
            ownedResult.RootElement[0].GetProperty("courseId").GetString());
        Assert.Equal(TestIdentities.Learner.AccountId.ToString("D"),
            ownedResult.RootElement[0].GetProperty("learnerAccountId").GetString());
        Assert.Equal(HttpStatusCode.NotFound, privateResponse.StatusCode);
        Assert.DoesNotContain(
            TestIdentities.Learner.AccountId.ToString("D"),
            await privateResponse.Content.ReadAsStringAsync());
        Assert.Equal(HttpStatusCode.NotFound, privateMutationResponse.StatusCode);
    }

    [Fact]
    public async Task LearnerEnrollmentViews_AreLimitedToAuthenticatedLearner()
    {
        using var authority = new TestOidcAuthority();
        using var factory = CreateFactory(authority);
        using var client = factory.CreateClient();
        var lifecycle = factory.Services.GetRequiredService<IAccountLifecycleService>();
        var otherLearner = await lifecycle.ProvisionVerifiedIdentityAsync(
            TestOidcAuthority.Issuer,
            "other-enrollment-viewer",
            AccountRole.Learner);
        await lifecycle.ActivateAsync(otherLearner.Account.Id);
        var courseService = factory.Services.GetRequiredService<ICourseService>();
        var ownCourse = await courseService.CreateCourseAsync(
            TestIdentities.Instructor.AccountId,
            "Learner's course");
        var otherCourse = await courseService.CreateCourseAsync(
            TestIdentities.Instructor.AccountId,
            "Other learner's course");
        var enrollmentService = factory.Services.GetRequiredService<IEnrollmentService>();
        await enrollmentService.EnrollLearnerAsync(
            TestIdentities.Instructor.AccountId,
            ownCourse.Id,
            TestIdentities.Learner.AccountId);
        await enrollmentService.EnrollLearnerAsync(
            TestIdentities.Instructor.AccountId,
            otherCourse.Id,
            otherLearner.Account.Id);

        using var listResponse = await SendEnrollmentManagementRequestAsync(
            client,
            HttpMethod.Get,
            $"/me/enrollments?learnerAccountId={otherLearner.Account.Id:D}&role=Instructor",
            TestIdentities.Learner.CreateToken(authority)!);
        using var listResult = JsonDocument.Parse(await listResponse.Content.ReadAsStringAsync());
        using var ownResponse = await SendEnrollmentManagementRequestAsync(
            client,
            HttpMethod.Get,
            $"/me/enrollments/{ownCourse.Id:D}",
            TestIdentities.Learner.CreateToken(authority)!);
        using var otherResponse = await SendEnrollmentManagementRequestAsync(
            client,
            HttpMethod.Get,
            $"/me/enrollments/{otherCourse.Id:D}",
            TestIdentities.Learner.CreateToken(authority)!);

        Assert.Equal(HttpStatusCode.OK, listResponse.StatusCode);
        Assert.Equal(1, listResult.RootElement.GetArrayLength());
        Assert.Equal(ownCourse.Id.ToString("D"),
            listResult.RootElement[0].GetProperty("courseId").GetString());
        Assert.Equal(TestIdentities.Learner.AccountId.ToString("D"),
            listResult.RootElement[0].GetProperty("learnerAccountId").GetString());
        Assert.Equal(HttpStatusCode.OK, ownResponse.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, otherResponse.StatusCode);
        Assert.DoesNotContain(
            otherLearner.Account.Id.ToString("D"),
            await otherResponse.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task EnrollmentStateChanges_FollowDecisionAndRepeatedActionConflicts()
    {
        using var authority = new TestOidcAuthority();
        using var factory = CreateFactory(authority);
        using var client = factory.CreateClient();
        var course = await factory.Services.GetRequiredService<ICourseService>()
            .CreateCourseAsync(TestIdentities.Instructor.AccountId, "Biology");
        var instructorToken = TestIdentities.Instructor.CreateToken(authority)!;
        var learnerToken = TestIdentities.Learner.CreateToken(authority)!;
        using var created = await SendEnrollmentRequestAsync(
            client,
            course.Id,
            instructorToken,
            new { learnerAccountId = TestIdentities.Learner.AccountId });

        using var suspended = await SendEnrollmentManagementRequestAsync(
            client,
            HttpMethod.Post,
            $"/courses/{course.Id:D}/enrollments/{TestIdentities.Learner.AccountId:D}/suspend",
            instructorToken);
        using var repeatedSuspend = await SendEnrollmentManagementRequestAsync(
            client,
            HttpMethod.Post,
            $"/courses/{course.Id:D}/enrollments/{TestIdentities.Learner.AccountId:D}/suspend",
            instructorToken);
        using var reactivated = await SendEnrollmentManagementRequestAsync(
            client,
            HttpMethod.Post,
            $"/courses/{course.Id:D}/enrollments/{TestIdentities.Learner.AccountId:D}/reactivate",
            instructorToken);
        using var removed = await SendEnrollmentManagementRequestAsync(
            client,
            HttpMethod.Post,
            $"/courses/{course.Id:D}/enrollments/{TestIdentities.Learner.AccountId:D}/remove",
            instructorToken);
        using var reEnrolled = await SendEnrollmentRequestAsync(
            client,
            course.Id,
            instructorToken,
            new { learnerAccountId = TestIdentities.Learner.AccountId });
        using var withdrawn = await SendEnrollmentManagementRequestAsync(
            client,
            HttpMethod.Post,
            $"/me/enrollments/{course.Id:D}/withdraw",
            learnerToken,
            new
            {
                learnerAccountId = Guid.NewGuid(),
                role = nameof(AccountRole.Instructor)
            });
        using var repeatedWithdrawal = await SendEnrollmentManagementRequestAsync(
            client,
            HttpMethod.Post,
            $"/me/enrollments/{course.Id:D}/withdraw",
            learnerToken);
        using var verifyScope = factory.Services.CreateScope();
        var database = verifyScope.ServiceProvider
            .GetRequiredService<Kokishin.Infrastructure.Persistence.KokishinDbContext>();

        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        Assert.Equal(HttpStatusCode.OK, suspended.StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, repeatedSuspend.StatusCode);
        Assert.Equal(HttpStatusCode.OK, reactivated.StatusCode);
        Assert.Equal(HttpStatusCode.OK, removed.StatusCode);
        Assert.Equal(HttpStatusCode.OK, reEnrolled.StatusCode);
        Assert.Equal(HttpStatusCode.OK, withdrawn.StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, repeatedWithdrawal.StatusCode);
        var enrollment = await database.Enrollments.SingleAsync();
        Assert.Equal(EnrollmentStatus.Withdrawn, enrollment.Status);
    }

    [Fact]
    public async Task EnrollmentEndpoints_RequireActiveAccountsAndCorrectRoles()
    {
        using var authority = new TestOidcAuthority();
        using var factory = CreateFactory(authority);
        using var client = factory.CreateClient();
        var course = await factory.Services.GetRequiredService<ICourseService>()
            .CreateCourseAsync(TestIdentities.Instructor.AccountId, "Biology");

        using var anonymousResponse = await client.GetAsync($"/courses/{course.Id:D}/enrollments");
        using var wrongRoleResponse = await SendEnrollmentManagementRequestAsync(
            client,
            HttpMethod.Get,
            $"/courses/{course.Id:D}/enrollments?role=Instructor",
            TestIdentities.Learner.CreateToken(authority)!);
        using var learnerOwnEnrollmentsResponse = await SendEnrollmentManagementRequestAsync(
            client,
            HttpMethod.Get,
            "/me/enrollments?learnerAccountId=another-account",
            TestIdentities.Learner.CreateToken(authority)!);
        await factory.Services.GetRequiredService<IAccountLifecycleService>()
            .DeactivateAsync(TestIdentities.Instructor.AccountId);
        using var inactiveResponse = await SendEnrollmentManagementRequestAsync(
            client,
            HttpMethod.Get,
            $"/courses/{course.Id:D}/enrollments",
            TestIdentities.Instructor.CreateToken(authority)!);
        await factory.Services.GetRequiredService<IAccountLifecycleService>()
            .DeactivateAsync(TestIdentities.Learner.AccountId);
        using var inactiveLearnerResponse = await SendEnrollmentManagementRequestAsync(
            client,
            HttpMethod.Get,
            "/me/enrollments",
            TestIdentities.Learner.CreateToken(authority)!);

        Assert.Equal(HttpStatusCode.Unauthorized, anonymousResponse.StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, wrongRoleResponse.StatusCode);
        Assert.Equal(HttpStatusCode.OK, learnerOwnEnrollmentsResponse.StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, inactiveResponse.StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, inactiveLearnerResponse.StatusCode);
    }

    [Fact]
    public async Task CourseList_InstructorSeesOnlyOwnedCourses_AndIgnoresClientOwnerId()
    {
        using var authority = new TestOidcAuthority();
        using var factory = CreateFactory(authority);
        using var client = factory.CreateClient();
        var lifecycle = factory.Services.GetRequiredService<IAccountLifecycleService>();
        var otherProvisioning = await lifecycle.ProvisionVerifiedIdentityAsync(
            TestOidcAuthority.Issuer,
            "another-instructor",
            AccountRole.Instructor);
        await lifecycle.ActivateAsync(otherProvisioning.Account.Id);
        var courseService = factory.Services.GetRequiredService<ICourseService>();
        var ownedCourse = await courseService.CreateCourseAsync(
            TestIdentities.Instructor.AccountId,
            "My Biology Course");
        var otherCourse = await courseService.CreateCourseAsync(
            otherProvisioning.Account.Id,
            "Private Chemistry Course");

        using var response = await SendRoleAuthorizationRequestAsync(
            client,
            $"/courses?ownerId={otherProvisioning.Account.Id:D}",
            TestIdentities.Instructor.CreateToken(authority)!);
        using var result = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var courses = result.RootElement.EnumerateArray().ToArray();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var course = Assert.Single(courses);
        Assert.Equal(ownedCourse.Id.ToString("D"), course.GetProperty("id").GetString());
        Assert.Equal("My Biology Course", course.GetProperty("title").GetString());
        Assert.DoesNotContain(
            courses,
            item => item.GetProperty("id").GetString() == otherCourse.Id.ToString("D"));
    }

    [Fact]
    public async Task CourseDetails_InstructorCanReadOwnedCourseButNotAnotherInstructorsCourse()
    {
        using var authority = new TestOidcAuthority();
        using var factory = CreateFactory(authority);
        using var client = factory.CreateClient();
        var lifecycle = factory.Services.GetRequiredService<IAccountLifecycleService>();
        var otherProvisioning = await lifecycle.ProvisionVerifiedIdentityAsync(
            TestOidcAuthority.Issuer,
            "private-course-instructor",
            AccountRole.Instructor);
        await lifecycle.ActivateAsync(otherProvisioning.Account.Id);
        var courseService = factory.Services.GetRequiredService<ICourseService>();
        var ownedCourse = await courseService.CreateCourseAsync(
            TestIdentities.Instructor.AccountId,
            "My Biology Course");
        var privateCourse = await courseService.CreateCourseAsync(
            otherProvisioning.Account.Id,
            "Private Chemistry Course");
        var token = TestIdentities.Instructor.CreateToken(authority)!;

        using var ownedResponse = await SendRoleAuthorizationRequestAsync(
            client,
            $"/courses/{ownedCourse.Id:D}",
            token);
        using var ownedResult = JsonDocument.Parse(await ownedResponse.Content.ReadAsStringAsync());
        using var privateResponse = await SendRoleAuthorizationRequestAsync(
            client,
            $"/courses/{privateCourse.Id:D}",
            token);

        Assert.Equal(HttpStatusCode.OK, ownedResponse.StatusCode);
        Assert.Equal(ownedCourse.Id.ToString("D"), ownedResult.RootElement.GetProperty("id").GetString());
        Assert.Equal("My Biology Course", ownedResult.RootElement.GetProperty("title").GetString());
        Assert.Equal(HttpStatusCode.NotFound, privateResponse.StatusCode);
        Assert.DoesNotContain(
            "Private Chemistry Course",
            await privateResponse.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task CourseUpdate_InstructorUpdatesOwnedInformationAndIgnoresIdentityFields()
    {
        using var authority = new TestOidcAuthority();
        using var factory = CreateFactory(authority);
        using var client = factory.CreateClient();
        var courseService = factory.Services.GetRequiredService<ICourseService>();
        var original = await courseService.CreateCourseAsync(
            TestIdentities.Instructor.AccountId,
            "Biology",
            "Original description.");
        var attackerId = Guid.NewGuid();
        var attackerOwnerId = Guid.NewGuid();

        using var response = await SendCourseUpdateRequestAsync(
            client,
            TestIdentities.Instructor.CreateToken(authority)!,
            original.Id,
            new
            {
                id = attackerId,
                ownerId = attackerOwnerId,
                title = "  Chemistry  ",
                description = "  Updated description.  ",
                status = CourseStatus.Published.ToString()
            });
        using var result = JsonDocument.Parse(await response.Content.ReadAsStringAsync());

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(original.Id.ToString("D"), result.RootElement.GetProperty("id").GetString());
        Assert.Equal(TestIdentities.Instructor.AccountId.ToString("D"),
            result.RootElement.GetProperty("ownerId").GetString());
        Assert.Equal("Chemistry", result.RootElement.GetProperty("title").GetString());
        Assert.Equal("Updated description.", result.RootElement.GetProperty("description").GetString());
        Assert.Equal(CourseStatus.Draft.ToString(), result.RootElement.GetProperty("status").GetString());
    }

    [Fact]
    public async Task CourseUpdate_InstructorCannotUpdateAnotherInstructorsCourse()
    {
        using var authority = new TestOidcAuthority();
        using var factory = CreateFactory(authority);
        using var client = factory.CreateClient();
        var lifecycle = factory.Services.GetRequiredService<IAccountLifecycleService>();
        var otherProvisioning = await lifecycle.ProvisionVerifiedIdentityAsync(
            TestOidcAuthority.Issuer,
            "private-course-update-instructor",
            AccountRole.Instructor);
        await lifecycle.ActivateAsync(otherProvisioning.Account.Id);
        var privateCourse = await factory.Services.GetRequiredService<ICourseService>()
            .CreateCourseAsync(otherProvisioning.Account.Id, "Private Chemistry Course");

        using var response = await SendCourseUpdateRequestAsync(
            client,
            TestIdentities.Instructor.CreateToken(authority)!,
            privateCourse.Id,
            new { title = "Updated private course" });

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.DoesNotContain("Private Chemistry Course", await response.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task CourseUpdate_MissingCourseReturnsNotFound()
    {
        using var authority = new TestOidcAuthority();
        using var factory = CreateFactory(authority);
        using var client = factory.CreateClient();

        using var response = await SendCourseUpdateRequestAsync(
            client,
            TestIdentities.Instructor.CreateToken(authority)!,
            Guid.NewGuid(),
            new { title = "Chemistry" });

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Theory]
    [InlineData("")]
    [InlineData("  ")]
    public async Task CourseUpdate_EmptyTitleReturnsValidationProblem(string title)
    {
        using var authority = new TestOidcAuthority();
        using var factory = CreateFactory(authority);
        using var client = factory.CreateClient();
        var course = await factory.Services.GetRequiredService<ICourseService>()
            .CreateCourseAsync(TestIdentities.Instructor.AccountId, "Biology");

        using var response = await SendCourseUpdateRequestAsync(
            client,
            TestIdentities.Instructor.CreateToken(authority)!,
            course.Id,
            new { title });
        using var problem = JsonDocument.Parse(await response.Content.ReadAsStringAsync());

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Contains(
            "title",
            problem.RootElement.GetProperty("errors").EnumerateObject().Select(error => error.Name));
    }

    [Fact]
    public async Task CourseUpdate_DescriptionExceedsLimitReturnsValidationProblem()
    {
        using var authority = new TestOidcAuthority();
        using var factory = CreateFactory(authority);
        using var client = factory.CreateClient();
        var course = await factory.Services.GetRequiredService<ICourseService>()
            .CreateCourseAsync(TestIdentities.Instructor.AccountId, "Biology");

        using var response = await SendCourseUpdateRequestAsync(
            client,
            TestIdentities.Instructor.CreateToken(authority)!,
            course.Id,
            new { title = "Chemistry", description = new string('a', Course.MaxDescriptionLength + 1) });
        using var problem = JsonDocument.Parse(await response.Content.ReadAsStringAsync());

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Contains(
            "description",
            problem.RootElement.GetProperty("errors").EnumerateObject().Select(error => error.Name));
    }

    [Fact]
    public async Task CourseUpdate_LearnerIsForbiddenAndAnonymousCallerIsUnauthorized()
    {
        using var authority = new TestOidcAuthority();
        using var factory = CreateFactory(authority);
        using var client = factory.CreateClient();
        var course = await factory.Services.GetRequiredService<ICourseService>()
            .CreateCourseAsync(TestIdentities.Instructor.AccountId, "Biology");

        using var learnerResponse = await SendCourseUpdateRequestAsync(
            client,
            TestIdentities.Learner.CreateToken(authority)!,
            course.Id,
            new { title = "Chemistry" });
        using var anonymousResponse = await client.PutAsJsonAsync(
            $"/courses/{course.Id:D}",
            new { title = "Chemistry" });

        Assert.Equal(HttpStatusCode.Forbidden, learnerResponse.StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, anonymousResponse.StatusCode);
    }

    [Fact]
    public async Task CoursePublicationStatus_OwnerCanPublishAndReturnCourseToDraft()
    {
        using var authority = new TestOidcAuthority();
        using var factory = CreateFactory(authority);
        using var client = factory.CreateClient();
        var course = await factory.Services.GetRequiredService<ICourseService>()
            .CreateCourseAsync(TestIdentities.Instructor.AccountId, "Biology", "An introduction.");
        var attackerOwnerId = Guid.NewGuid();
        var token = TestIdentities.Instructor.CreateToken(authority)!;

        using var publishResponse = await SendCoursePublicationStatusRequestAsync(
            client,
            token,
            course.Id,
            new { status = CourseStatus.Published.ToString(), ownerId = attackerOwnerId });
        using var publishedResult = JsonDocument.Parse(await publishResponse.Content.ReadAsStringAsync());
        using var draftResponse = await SendCoursePublicationStatusRequestAsync(
            client,
            token,
            course.Id,
            new { status = CourseStatus.Draft.ToString() });
        using var draftResult = JsonDocument.Parse(await draftResponse.Content.ReadAsStringAsync());
        using var databaseScope = factory.Services.CreateScope();
        var database = databaseScope.ServiceProvider
            .GetRequiredService<Kokishin.Infrastructure.Persistence.KokishinDbContext>();
        var persisted = await database.Courses.SingleAsync(existing => existing.Id == course.Id);

        Assert.Equal(HttpStatusCode.OK, publishResponse.StatusCode);
        Assert.Equal(CourseStatus.Published.ToString(),
            publishedResult.RootElement.GetProperty("status").GetString());
        Assert.Equal(TestIdentities.Instructor.AccountId.ToString("D"),
            publishedResult.RootElement.GetProperty("ownerId").GetString());
        Assert.Equal("Biology", publishedResult.RootElement.GetProperty("title").GetString());
        Assert.Equal("An introduction.", publishedResult.RootElement.GetProperty("description").GetString());
        Assert.Equal(HttpStatusCode.OK, draftResponse.StatusCode);
        Assert.Equal(CourseStatus.Draft.ToString(), draftResult.RootElement.GetProperty("status").GetString());
        Assert.Equal(TestIdentities.Instructor.AccountId, persisted.OwnerId);
        Assert.Equal(CourseStatus.Draft, persisted.Status);
    }

    [Fact]
    public async Task CoursePublicationStatus_InstructorCannotChangeAnotherInstructorsCourse()
    {
        using var authority = new TestOidcAuthority();
        using var factory = CreateFactory(authority);
        using var client = factory.CreateClient();
        var lifecycle = factory.Services.GetRequiredService<IAccountLifecycleService>();
        var otherProvisioning = await lifecycle.ProvisionVerifiedIdentityAsync(
            TestOidcAuthority.Issuer,
            "course-publication-owner",
            AccountRole.Instructor);
        await lifecycle.ActivateAsync(otherProvisioning.Account.Id);
        var course = await factory.Services.GetRequiredService<ICourseService>()
            .CreateCourseAsync(otherProvisioning.Account.Id, "Private Biology");

        using var response = await SendCoursePublicationStatusRequestAsync(
            client,
            TestIdentities.Instructor.CreateToken(authority)!,
            course.Id,
            new { status = CourseStatus.Published.ToString() });
        using var databaseScope = factory.Services.CreateScope();
        var database = databaseScope.ServiceProvider
            .GetRequiredService<Kokishin.Infrastructure.Persistence.KokishinDbContext>();
        var persisted = await database.Courses.SingleAsync(existing => existing.Id == course.Id);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Equal(CourseStatus.Draft, course.Status);
        Assert.Equal(CourseStatus.Draft, persisted.Status);
    }

    [Fact]
    public async Task CoursePublicationStatus_LearnerIsForbiddenAndAnonymousCallerIsUnauthorized()
    {
        using var authority = new TestOidcAuthority();
        using var factory = CreateFactory(authority);
        using var client = factory.CreateClient();
        var course = await factory.Services.GetRequiredService<ICourseService>()
            .CreateCourseAsync(TestIdentities.Instructor.AccountId, "Biology");

        using var learnerResponse = await SendCoursePublicationStatusRequestAsync(
            client,
            TestIdentities.Learner.CreateToken(authority)!,
            course.Id,
            new { status = CourseStatus.Published.ToString() });
        using var anonymousResponse = await client.PutAsJsonAsync(
            $"/courses/{course.Id:D}/publication-status",
            new { status = CourseStatus.Published.ToString() });

        Assert.Equal(HttpStatusCode.Forbidden, learnerResponse.StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, anonymousResponse.StatusCode);
    }

    [Fact]
    public async Task CoursePublicationStatus_DeactivatedInstructorIsUnauthorized()
    {
        using var authority = new TestOidcAuthority();
        using var factory = CreateFactory(authority);
        using var client = factory.CreateClient();
        var course = await factory.Services.GetRequiredService<ICourseService>()
            .CreateCourseAsync(TestIdentities.Instructor.AccountId, "Biology");
        var token = TestIdentities.Instructor.CreateToken(authority)!;
        await factory.Services.GetRequiredService<IAccountLifecycleService>()
            .DeactivateAsync(TestIdentities.Instructor.AccountId);

        using var response = await SendCoursePublicationStatusRequestAsync(
            client,
            token,
            course.Id,
            new { status = CourseStatus.Published.ToString() });

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Equal(CourseStatus.Draft, course.Status);
    }

    [Fact]
    public async Task CoursePublicationStatus_UnsupportedAndRepeatedStatusReturnValidationProblem()
    {
        using var authority = new TestOidcAuthority();
        using var factory = CreateFactory(authority);
        using var client = factory.CreateClient();
        var course = await factory.Services.GetRequiredService<ICourseService>()
            .CreateCourseAsync(TestIdentities.Instructor.AccountId, "Biology");
        var token = TestIdentities.Instructor.CreateToken(authority)!;

        using var unsupportedResponse = await SendCoursePublicationStatusRequestAsync(
            client,
            token,
            course.Id,
            new { status = "Archived" });
        using var unsupportedProblem = JsonDocument.Parse(
            await unsupportedResponse.Content.ReadAsStringAsync());
        using var repeatedResponse = await SendCoursePublicationStatusRequestAsync(
            client,
            token,
            course.Id,
            new { status = CourseStatus.Draft.ToString() });
        using var repeatedProblem = JsonDocument.Parse(await repeatedResponse.Content.ReadAsStringAsync());

        Assert.Equal(HttpStatusCode.BadRequest, unsupportedResponse.StatusCode);
        Assert.Contains("status",
            unsupportedProblem.RootElement.GetProperty("errors").EnumerateObject()
                .Select(error => error.Name));
        Assert.Equal(HttpStatusCode.BadRequest, repeatedResponse.StatusCode);
        Assert.Contains("status",
            repeatedProblem.RootElement.GetProperty("errors").EnumerateObject()
                .Select(error => error.Name));
    }

    [Fact]
    public async Task CourseList_InstructorWithoutCoursesReceivesEmptyCollection()
    {
        using var authority = new TestOidcAuthority();
        using var factory = CreateFactory(authority);
        using var client = factory.CreateClient();

        using var response = await SendRoleAuthorizationRequestAsync(
            client,
            $"/courses?ownerId={TestIdentities.Learner.AccountId:D}",
            TestIdentities.Instructor.CreateToken(authority)!);
        using var result = JsonDocument.Parse(await response.Content.ReadAsStringAsync());

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Empty(result.RootElement.EnumerateArray());
    }

    [Fact]
    public async Task CourseManagementQueries_LearnerIsForbiddenAndAnonymousCallerIsUnauthorized()
    {
        using var authority = new TestOidcAuthority();
        using var factory = CreateFactory(authority);
        using var client = factory.CreateClient();
        var course = await factory.Services.GetRequiredService<ICourseService>()
            .CreateCourseAsync(TestIdentities.Instructor.AccountId, "Biology");

        using var learnerListResponse = await SendRoleAuthorizationRequestAsync(
            client,
            "/courses",
            TestIdentities.Learner.CreateToken(authority)!);
        using var learnerDetailsResponse = await SendRoleAuthorizationRequestAsync(
            client,
            $"/courses/{course.Id:D}",
            TestIdentities.Learner.CreateToken(authority)!);
        using var anonymousListResponse = await client.GetAsync("/courses");
        using var anonymousDetailsResponse = await client.GetAsync($"/courses/{course.Id:D}");

        Assert.Equal(HttpStatusCode.Forbidden, learnerListResponse.StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, learnerDetailsResponse.StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, anonymousListResponse.StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, anonymousDetailsResponse.StatusCode);
    }

    [Theory]
    [InlineData("")]
    [InlineData("  ")]
    public async Task CourseCreation_EmptyTitleReturnsValidationProblem(string title)
    {
        using var authority = new TestOidcAuthority();
        using var factory = CreateFactory(authority);
        using var client = factory.CreateClient();

        using var response = await SendCourseRequestAsync(
            client,
            TestIdentities.Instructor.CreateToken(authority)!,
            new { title });
        using var problem = JsonDocument.Parse(await response.Content.ReadAsStringAsync());

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Contains(
            "title",
            problem.RootElement.GetProperty("errors").EnumerateObject().Select(error => error.Name));
    }

    [Fact]
    public async Task CourseCreation_MissingTitleReturnsValidationProblem()
    {
        using var authority = new TestOidcAuthority();
        using var factory = CreateFactory(authority);
        using var client = factory.CreateClient();
        using var request = new HttpRequestMessage(HttpMethod.Post, "/courses")
        {
            Content = JsonContent.Create(new { description = "An introduction." })
        };
        request.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue(
            "Bearer",
            TestIdentities.Instructor.CreateToken(authority)!);

        using var response = await client.SendAsync(request);
        using var problem = JsonDocument.Parse(await response.Content.ReadAsStringAsync());

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Contains(
            "title",
            problem.RootElement.GetProperty("errors").EnumerateObject().Select(error => error.Name));
    }

    [Fact]
    public async Task CourseCreation_DescriptionExceedsLimitReturnsValidationProblem()
    {
        using var authority = new TestOidcAuthority();
        using var factory = CreateFactory(authority);
        using var client = factory.CreateClient();

        using var response = await SendCourseRequestAsync(
            client,
            TestIdentities.Instructor.CreateToken(authority)!,
            new { title = "Biology", description = new string('a', Course.MaxDescriptionLength + 1) });
        using var problem = JsonDocument.Parse(await response.Content.ReadAsStringAsync());

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Contains(
            "description",
            problem.RootElement.GetProperty("errors").EnumerateObject().Select(error => error.Name));
    }

    private static Guid ActiveAccountId => TestIdentities.Learner.AccountId;

    private static HttpRequestMessage CreateIdentityRequest(
        TestOidcAuthority authority,
        TestIdentity identity)
    {
        var request = new HttpRequestMessage(HttpMethod.Get, "/test/identity");
        var token = identity.CreateToken(authority);
        if (token is not null)
        {
            request.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue(
                "Bearer",
                token);
        }

        return request;
    }

    private static void AssertSafeAuthenticationFailure(HttpResponseMessage response, string body)
    {
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
        using var problem = JsonDocument.Parse(body);
        Assert.Equal(
            (int)HttpStatusCode.Unauthorized,
            problem.RootElement.GetProperty("status").GetInt32());
        Assert.False(string.IsNullOrWhiteSpace(problem.RootElement.GetProperty("title").GetString()));
        Assert.Matches("^[0-9a-f]{32}$", problem.RootElement.GetProperty("traceId").GetString());
        Assert.DoesNotContain(TestOidcAuthority.ClientSecret, body);
        Assert.DoesNotContain("Exception", body);
        Assert.DoesNotContain(" at ", body);
    }

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

    private static async Task<HttpResponseMessage> SendCourseRequestAsync(
        HttpClient client,
        string token,
        object body)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, "/courses")
        {
            Content = JsonContent.Create(body)
        };
        request.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", token);
        return await client.SendAsync(request);
    }

    private static async Task<HttpResponseMessage> SendEnrollmentRequestAsync(
        HttpClient client,
        Guid courseId,
        string? token,
        object body)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, $"/courses/{courseId:D}/enrollments")
        {
            Content = JsonContent.Create(body)
        };
        if (token is not null)
        {
            request.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", token);
        }

        return await client.SendAsync(request);
    }

    private static async Task<HttpResponseMessage> SendEnrollmentManagementRequestAsync(
        HttpClient client,
        HttpMethod method,
        string path,
        string? token,
        object? body = null)
    {
        using var request = new HttpRequestMessage(method, path);
        if (token is not null)
        {
            request.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", token);
        }

        if (body is not null)
        {
            request.Content = JsonContent.Create(body);
        }

        return await client.SendAsync(request);
    }

    private static async Task<HttpResponseMessage> SendCourseUpdateRequestAsync(
        HttpClient client,
        string token,
        Guid courseId,
        object body)
    {
        using var request = new HttpRequestMessage(HttpMethod.Put, $"/courses/{courseId:D}")
        {
            Content = JsonContent.Create(body)
        };
        request.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", token);
        return await client.SendAsync(request);
    }

    private static async Task<HttpResponseMessage> SendCoursePublicationStatusRequestAsync(
        HttpClient client,
        string token,
        Guid courseId,
        object body)
    {
        using var request = new HttpRequestMessage(
            HttpMethod.Put,
            $"/courses/{courseId:D}/publication-status")
        {
            Content = JsonContent.Create(body)
        };
        request.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", token);
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

            AssertSafeAuthenticationFailure(response, body);
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
            var lifecycle = scope.ServiceProvider.GetRequiredService<IAccountLifecycleService>();
            foreach (var identity in new[] { TestIdentities.Instructor, TestIdentities.Learner })
            {
                var provisioning = lifecycle.ProvisionVerifiedIdentityAsync(
                        TestOidcAuthority.Issuer,
                        identity.Subject!,
                        identity.Role!.Value)
                    .GetAwaiter()
                    .GetResult();
                if (provisioning.Outcome == AccountProvisioningOutcome.Created)
                {
                    lifecycle.ActivateAsync(provisioning.Account.Id).GetAwaiter().GetResult();
                }
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

    private sealed record TestIdentity(string? Subject, AccountRole? Role)
    {
        public Guid AccountId => new Account(
            TestOidcAuthority.Issuer,
            Subject ?? throw new InvalidOperationException("Anonymous identities do not have accounts."),
            Role ?? throw new InvalidOperationException("Anonymous identities do not have account roles.")).Id;

        public string? CreateToken(TestOidcAuthority authority, string? nameIdentifier = null) =>
            Subject is null
                ? null
                : authority.CreateToken(Subject, nameIdentifier: nameIdentifier, role: Role?.ToString());
    }

    private static class TestIdentities
    {
        public static readonly TestIdentity Anonymous = new(null, null);
        public static readonly TestIdentity Instructor = new("instructor-subject", AccountRole.Instructor);
        public static readonly TestIdentity Learner = new("provider-subject", AccountRole.Learner);

        public static TestIdentity For(AccountRole role) => role switch
        {
            AccountRole.Instructor => Instructor,
            AccountRole.Learner => Learner,
            _ => throw new ArgumentOutOfRangeException(nameof(role), role, null)
        };
    }
}
