using Kokishin.Domain.Accounts;

namespace Kokishin.Application.Accounts;

public interface IAccountStore
{
    Task<Account?> FindByIdentityAsync(
        string issuer,
        string subject,
        CancellationToken cancellationToken = default);

    Task AddAsync(Account account, CancellationToken cancellationToken = default);
}
