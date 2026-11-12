using Microsoft.EntityFrameworkCore;

namespace Kokishin.Infrastructure.Persistence.Sessions;

public sealed class SqliteSessionTicketStore(
    IDbContextFactory<KokishinDbContext> contextFactory) : IDurableSessionTicketStore
{
    public async Task StoreAsync(
        string key,
        byte[] protectedTicket,
        DateTimeOffset expiresAt,
        CancellationToken cancellationToken = default)
    {
        await using var dbContext = await contextFactory.CreateDbContextAsync(cancellationToken);
        var now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        await dbContext.SessionTickets
            .Where(ticket => ticket.ExpiresAtUnixSeconds <= now)
            .ExecuteDeleteAsync(cancellationToken);

        await dbContext.Database.ExecuteSqlInterpolatedAsync(
            $"""
            INSERT INTO "SessionTickets" ("Key", "ProtectedTicket", "ExpiresAtUnixSeconds")
            VALUES ({key}, {protectedTicket}, {expiresAt.ToUnixTimeSeconds()})
            ON CONFLICT("Key") DO UPDATE SET
                "ProtectedTicket" = excluded."ProtectedTicket",
                "ExpiresAtUnixSeconds" = excluded."ExpiresAtUnixSeconds";
            """,
            cancellationToken);
    }

    public async Task<byte[]?> RetrieveAsync(string key, CancellationToken cancellationToken = default)
    {
        await using var dbContext = await contextFactory.CreateDbContextAsync(cancellationToken);
        var ticket = await dbContext.SessionTickets
            .AsNoTracking()
            .SingleOrDefaultAsync(ticket => ticket.Key == key, cancellationToken);
        if (ticket is null)
        {
            return null;
        }

        if (ticket.ExpiresAtUnixSeconds <= DateTimeOffset.UtcNow.ToUnixTimeSeconds())
        {
            await RemoveAsync(key, cancellationToken);
            return null;
        }

        return ticket.ProtectedTicket;
    }

    public async Task RemoveAsync(string key, CancellationToken cancellationToken = default)
    {
        await using var dbContext = await contextFactory.CreateDbContextAsync(cancellationToken);
        await dbContext.SessionTickets
            .Where(ticket => ticket.Key == key)
            .ExecuteDeleteAsync(cancellationToken);
    }
}
