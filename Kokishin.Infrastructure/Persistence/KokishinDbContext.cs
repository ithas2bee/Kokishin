using Kokishin.Infrastructure.Persistence.Sessions;
using Microsoft.EntityFrameworkCore;

namespace Kokishin.Infrastructure.Persistence;

public sealed class KokishinDbContext(DbContextOptions<KokishinDbContext> options) : DbContext(options)
{
    public DbSet<SessionTicketRecord> SessionTickets => Set<SessionTicketRecord>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<SessionTicketRecord>(entity =>
        {
            entity.ToTable("SessionTickets");
            entity.HasKey(ticket => ticket.Key);
            entity.Property(ticket => ticket.Key).HasMaxLength(32);
            entity.Property(ticket => ticket.ProtectedTicket).IsRequired();
            entity.HasIndex(ticket => ticket.ExpiresAtUnixSeconds);
        });
    }
}
