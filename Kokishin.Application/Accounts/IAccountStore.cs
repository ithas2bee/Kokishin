using Kokishin.Domain.Accounts;

namespace Kokishin.Application.Accounts;

public interface IAccountStore
{
    Task<Account?> FindByIdAsync(Guid id, CancellationToken cancellationToken = default);

    Task<Account?> FindByIdentityAsync(
        string issuer,
        string subject,
        CancellationToken cancellationToken = default);

    Task AddAsync(Account account, CancellationToken cancellationToken = default);

    Task<Account?> AddIfAbsentAsync(Account account, CancellationToken cancellationToken = default);

    Task UpdateAsync(Account account, CancellationToken cancellationToken = default);
}
