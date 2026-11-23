using Kokishin.Application.Enrollments;
using Kokishin.Domain.Enrollments;
using Microsoft.EntityFrameworkCore;

namespace Kokishin.Infrastructure.Persistence.Enrollments;

public sealed class SqliteEnrollmentStore(IDbContextFactory<KokishinDbContext> contextFactory)
    : IEnrollmentStore
{
    public async Task<Enrollment?> FindAsync(
        Guid courseId,
        Guid learnerAccountId,
        CancellationToken cancellationToken = default)
    {
        await using var dbContext = await contextFactory.CreateDbContextAsync(cancellationToken);
        return await dbContext.Enrollments
            .AsNoTracking()
            .SingleOrDefaultAsync(
                enrollment =>
                    enrollment.CourseId == courseId &&
                    enrollment.LearnerAccountId == learnerAccountId,
                cancellationToken);
    }

    public async Task<Enrollment?> AddIfAbsentAsync(
        Enrollment enrollment,
        CancellationToken cancellationToken = default)
    {
        var existing = await FindAsync(
            enrollment.CourseId,
            enrollment.LearnerAccountId,
            cancellationToken);
        if (existing is not null)
        {
            return existing;
        }

        try
        {
            await using var dbContext = await contextFactory.CreateDbContextAsync(cancellationToken);
            dbContext.Enrollments.Add(enrollment);
            await dbContext.SaveChangesAsync(cancellationToken);
            return null;
        }
        catch (DbUpdateException)
        {
            existing = await FindAsync(
                enrollment.CourseId,
                enrollment.LearnerAccountId,
                cancellationToken);
            if (existing is null)
            {
                throw;
            }

            return existing;
        }
    }

    public async Task UpdateAsync(Enrollment enrollment, CancellationToken cancellationToken = default)
    {
        await using var dbContext = await contextFactory.CreateDbContextAsync(cancellationToken);
        dbContext.Enrollments.Attach(enrollment);
        dbContext.Entry(enrollment).Property(existing => existing.Status).IsModified = true;
        await dbContext.SaveChangesAsync(cancellationToken);
    }
}
