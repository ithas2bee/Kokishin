using Kokishin.Domain.Accounts;
using Kokishin.Infrastructure.Persistence.Sessions;
using Microsoft.EntityFrameworkCore;

namespace Kokishin.Infrastructure.Persistence;

public sealed class KokishinDbContext(DbContextOptions<KokishinDbContext> options) : DbContext(options)
{
    public DbSet<Account> Accounts => Set<Account>();

    public DbSet<SessionTicketRecord> SessionTickets => Set<SessionTicketRecord>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<Account>(entity =>
        {
            entity.ToTable("Accounts");
            entity.HasKey(account => account.Id);
            entity.Property(account => account.Id).ValueGeneratedNever();
            entity.Property(account => account.Issuer).IsRequired();
            entity.Property(account => account.Subject).IsRequired();
            entity.HasIndex(account => new { account.Issuer, account.Subject }).IsUnique();
        });

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
