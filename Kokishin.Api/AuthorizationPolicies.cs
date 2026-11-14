using System.Security.Claims;
using Kokishin.Application.Accounts;
using Kokishin.Domain.Accounts;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;

namespace Kokishin.Api;

public static class AuthorizationPolicies
{
    public const string Instructor = "AccountRole:Instructor";
    public const string Learner = "AccountRole:Learner";
}

internal sealed record AccountRoleRequirement(AccountRole Role) : IAuthorizationRequirement;

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
