namespace Kokishin.Infrastructure.Persistence.Sessions;

public sealed class SessionTicketRecord
{
    public required string Key { get; set; }

    public required byte[] ProtectedTicket { get; set; }

    public long ExpiresAtUnixSeconds { get; set; }
}
