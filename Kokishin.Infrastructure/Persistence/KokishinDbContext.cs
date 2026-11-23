using Kokishin.Domain.Accounts;
using Kokishin.Domain.Courses;
using Kokishin.Domain.Enrollments;
using Kokishin.Domain.Profiles;
using Kokishin.Infrastructure.Persistence.Sessions;
using Microsoft.EntityFrameworkCore;

namespace Kokishin.Infrastructure.Persistence;

public sealed class KokishinDbContext(DbContextOptions<KokishinDbContext> options) : DbContext(options)
{
    public DbSet<Account> Accounts => Set<Account>();

    public DbSet<Course> Courses => Set<Course>();

    public DbSet<Enrollment> Enrollments => Set<Enrollment>();

    public DbSet<Profile> Profiles => Set<Profile>();

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
            entity.Property(account => account.Role)
                .HasConversion<string>()
                .IsRequired();
            entity.HasIndex(account => new { account.Issuer, account.Subject }).IsUnique();
        });

        modelBuilder.Entity<Profile>(entity =>
        {
            entity.ToTable("Profiles");
            entity.HasKey(profile => profile.AccountId);
            entity.Property(profile => profile.AccountId).ValueGeneratedNever();
            entity.Property(profile => profile.DisplayName)
                .HasMaxLength(Profile.MaxDisplayNameLength)
                .IsRequired();
            entity.Property(profile => profile.Description)
                .HasMaxLength(Profile.MaxDescriptionLength);
            entity.HasOne<Account>()
                .WithOne()
                .HasForeignKey<Profile>(profile => profile.AccountId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<Course>(entity =>
        {
            entity.ToTable("Courses");
            entity.HasKey(course => course.Id);
            entity.Property(course => course.Id).ValueGeneratedNever();
            entity.Property(course => course.OwnerId).IsRequired();
            entity.Property(course => course.Title)
                .HasMaxLength(Course.MaxTitleLength)
                .IsRequired();
            entity.Property(course => course.Description)
                .HasMaxLength(Course.MaxDescriptionLength);
            entity.Property(course => course.Status)
                .HasConversion<string>()
                .IsRequired();
            entity.HasOne<Account>()
                .WithMany()
                .HasForeignKey(course => course.OwnerId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<Enrollment>(entity =>
        {
            entity.ToTable("Enrollments");
            entity.HasKey(enrollment => new { enrollment.CourseId, enrollment.LearnerAccountId });
            entity.Property(enrollment => enrollment.Status)
                .HasConversion<string>()
                .IsRequired();
            entity.HasOne<Course>()
                .WithMany()
                .HasForeignKey(enrollment => enrollment.CourseId)
                .OnDelete(DeleteBehavior.Cascade);
            entity.HasOne<Account>()
                .WithMany()
                .HasForeignKey(enrollment => enrollment.LearnerAccountId)
                .OnDelete(DeleteBehavior.Cascade);
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
