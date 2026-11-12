using Kokishin.Domain.Accounts;

namespace Kokishin.Application.Accounts;

public sealed class AccountLifecycleService(IAccountStore accountStore) : IAccountLifecycleService
{
    public async Task<AccountProvisioningResult> ProvisionVerifiedIdentityAsync(
        string issuer,
        string subject,
        AccountRole role,
        CancellationToken cancellationToken = default)
    {
        var account = new Account(issuer, subject, role);
        var existing = await accountStore.AddIfAbsentAsync(account, cancellationToken);
        return existing is null
            ? new AccountProvisioningResult(account, AccountProvisioningOutcome.Created)
            : new AccountProvisioningResult(existing, AccountProvisioningOutcome.AlreadyExists);
    }

    public async Task ActivateAsync(Guid accountId, CancellationToken cancellationToken = default)
    {
        var account = await FindRequiredAccountAsync(accountId, cancellationToken);
        account.Activate();
        await accountStore.UpdateAsync(account, cancellationToken);
    }

    public async Task DeactivateAsync(Guid accountId, CancellationToken cancellationToken = default)
    {
        var account = await FindRequiredAccountAsync(accountId, cancellationToken);
        account.Deactivate();
        await accountStore.UpdateAsync(account, cancellationToken);
    }

    public async Task<bool> IsActiveAsync(Guid accountId, CancellationToken cancellationToken = default) =>
        (await accountStore.FindByIdAsync(accountId, cancellationToken))?.Status == AccountStatus.Active;

    public async Task<AccountRole?> GetRoleAsync(Guid accountId, CancellationToken cancellationToken = default) =>
        (await accountStore.FindByIdAsync(accountId, cancellationToken))?.Role;

    private async Task<Account> FindRequiredAccountAsync(Guid accountId, CancellationToken cancellationToken) =>
        await accountStore.FindByIdAsync(accountId, cancellationToken)
        ?? throw new KeyNotFoundException($"Account '{accountId}' was not found.");
}
