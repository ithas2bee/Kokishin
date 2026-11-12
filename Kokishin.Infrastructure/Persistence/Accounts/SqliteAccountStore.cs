using Kokishin.Application.Accounts;
using Kokishin.Domain.Accounts;
using Microsoft.EntityFrameworkCore;

namespace Kokishin.Infrastructure.Persistence.Accounts;

public sealed class SqliteAccountStore(IDbContextFactory<KokishinDbContext> contextFactory) : IAccountStore
{
    public async Task<Account?> FindByIdAsync(Guid id, CancellationToken cancellationToken = default)
    {
        await using var dbContext = await contextFactory.CreateDbContextAsync(cancellationToken);
        return await dbContext.Accounts
            .AsNoTracking()
            .SingleOrDefaultAsync(account => account.Id == id, cancellationToken);
    }

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

    public async Task<Account?> AddIfAbsentAsync(Account account, CancellationToken cancellationToken = default)
    {
        var existing = await FindByIdentityAsync(account.Issuer, account.Subject, cancellationToken);
        if (existing is not null)
        {
            return existing;
        }

        try
        {
            await AddAsync(account, cancellationToken);
            return null;
        }
        catch (DbUpdateException)
        {
            existing = await FindByIdentityAsync(account.Issuer, account.Subject, cancellationToken);
            if (existing is null)
            {
                throw;
            }

            return existing;
        }
    }

    public async Task UpdateAsync(Account account, CancellationToken cancellationToken = default)
    {
        await using var dbContext = await contextFactory.CreateDbContextAsync(cancellationToken);
        dbContext.Accounts.Update(account);
        await dbContext.SaveChangesAsync(cancellationToken);
    }
}
