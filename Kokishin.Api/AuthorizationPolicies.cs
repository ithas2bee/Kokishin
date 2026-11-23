using System.Security.Claims;
using Kokishin.Application.Accounts;
using Kokishin.Application.Enrollments;
using Kokishin.Domain.Accounts;
using Kokishin.Domain.Courses;
using Kokishin.Domain.Enrollments;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;

namespace Kokishin.Api;

public static class AuthorizationPolicies
{
    public const string Instructor = "AccountRole:Instructor";
    public const string Learner = "AccountRole:Learner";
    public const string CourseAccess = "CourseAccess";
}

internal sealed record AccountRoleRequirement(AccountRole Role) : IAuthorizationRequirement;

internal sealed record CourseAccessRequirement : IAuthorizationRequirement;

internal sealed class AccountRoleAuthorizationHandler(
    IAccountLifecycleService accountLifecycleService)
    : AuthorizationHandler<AccountRoleRequirement>
{
    protected override async Task HandleRequirementAsync(
        AuthorizationHandlerContext context,
        AccountRoleRequirement requirement)
    {
        if (!Guid.TryParse(context.User.FindFirstValue(ClaimTypes.NameIdentifier), out var accountId))
        {
            return;
        }

        var cancellationToken = (context.Resource as HttpContext)?.RequestAborted ?? CancellationToken.None;
        var role = await accountLifecycleService.GetRoleAsync(accountId, cancellationToken);
        if (role == requirement.Role)
        {
            context.Succeed(requirement);
        }
    }
}

internal sealed class CourseAccessAuthorizationHandler(
    IAccountLifecycleService accountLifecycleService,
    IEnrollmentService enrollmentService)
    : AuthorizationHandler<CourseAccessRequirement, Course>
{
    protected override async Task HandleRequirementAsync(
        AuthorizationHandlerContext context,
        CourseAccessRequirement requirement,
        Course course)
    {
        if (!Guid.TryParse(context.User.FindFirstValue(ClaimTypes.NameIdentifier), out var accountId) ||
            !await accountLifecycleService.IsActiveAsync(accountId))
        {
            return;
        }

        var role = await accountLifecycleService.GetRoleAsync(accountId);
        if (role == AccountRole.Instructor && course.OwnerId == accountId)
        {
            context.Succeed(requirement);
            return;
        }

        if (role != AccountRole.Learner || course.Status != CourseStatus.Published)
        {
            return;
        }

        var enrollment = await enrollmentService.GetForLearnerAsync(accountId, course.Id);
        if (enrollment?.Status == EnrollmentStatus.Active)
        {
            context.Succeed(requirement);
        }
    }
}
