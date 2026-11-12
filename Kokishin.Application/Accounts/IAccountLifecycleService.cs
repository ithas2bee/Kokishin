using Kokishin.Domain.Accounts;

namespace Kokishin.Application.Accounts;

public interface IAccountLifecycleService
{
    Task<AccountProvisioningResult> ProvisionVerifiedIdentityAsync(
        string issuer,
        string subject,
        AccountRole role,
        CancellationToken cancellationToken = default);

    Task ActivateAsync(Guid accountId, CancellationToken cancellationToken = default);

    Task DeactivateAsync(Guid accountId, CancellationToken cancellationToken = default);

    Task<bool> IsActiveAsync(Guid accountId, CancellationToken cancellationToken = default);

    Task<AccountRole?> GetRoleAsync(Guid accountId, CancellationToken cancellationToken = default);
}
