using Kokishin.Application.Accounts;
using Kokishin.Domain.Accounts;
using Microsoft.EntityFrameworkCore;

namespace Kokishin.Infrastructure.Persistence.Accounts;

public sealed class SqliteAccountStore(IDbContextFactory<KokishinDbContext> contextFactory) : IAccountStore
{
    public async Task<Account?> FindByIdentityAsync(
        string issuer,
        string subject,
        CancellationToken cancellationToken = default)
    {
        await using var dbContext = await contextFactory.CreateDbContextAsync(cancellationToken);
        return await dbContext.Accounts
            .AsNoTracking()
            .SingleOrDefaultAsync(
                account => account.Issuer == issuer && account.Subject == subject,
                cancellationToken);
    }

    public async Task AddAsync(Account account, CancellationToken cancellationToken = default)
    {
        await using var dbContext = await contextFactory.CreateDbContextAsync(cancellationToken);
        dbContext.Accounts.Add(account);
        await dbContext.SaveChangesAsync(cancellationToken);
    }
}
