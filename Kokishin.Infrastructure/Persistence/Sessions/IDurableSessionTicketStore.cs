namespace Kokishin.Infrastructure.Persistence.Sessions;

public interface IDurableSessionTicketStore
{
    Task StoreAsync(
        string key,
        byte[] protectedTicket,
        DateTimeOffset expiresAt,
        CancellationToken cancellationToken = default);

    Task<byte[]?> RetrieveAsync(string key, CancellationToken cancellationToken = default);

    Task RemoveAsync(string key, CancellationToken cancellationToken = default);
}
