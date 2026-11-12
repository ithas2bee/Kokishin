using Kokishin.Application.Profiles;
using Kokishin.Domain.Profiles;
using Microsoft.EntityFrameworkCore;

namespace Kokishin.Infrastructure.Persistence.Profiles;

public sealed class SqliteProfileStore(IDbContextFactory<KokishinDbContext> contextFactory) : IProfileStore
{
    public async Task<Profile?> FindByAccountIdAsync(
        Guid accountId,
        CancellationToken cancellationToken = default)
    {
        await using var dbContext = await contextFactory.CreateDbContextAsync(cancellationToken);
        return await dbContext.Profiles
            .AsNoTracking()
            .SingleOrDefaultAsync(profile => profile.AccountId == accountId, cancellationToken);
    }

    public async Task SaveAsync(Profile profile, CancellationToken cancellationToken = default)
    {
        await using var dbContext = await contextFactory.CreateDbContextAsync(cancellationToken);
        await dbContext.Database.ExecuteSqlInterpolatedAsync(
            $"""
             INSERT INTO "Profiles" ("AccountId", "DisplayName", "Description")
             VALUES ({profile.AccountId}, {profile.DisplayName}, {profile.Description})
             ON CONFLICT("AccountId") DO UPDATE SET
                 "DisplayName" = excluded."DisplayName",
                 "Description" = excluded."Description"
             """,
            cancellationToken);
    }
}
