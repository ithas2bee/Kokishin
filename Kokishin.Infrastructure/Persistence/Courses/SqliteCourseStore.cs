using Kokishin.Application.Courses;
using Kokishin.Domain.Courses;
using Microsoft.EntityFrameworkCore;

namespace Kokishin.Infrastructure.Persistence.Courses;

public sealed class SqliteCourseStore(IDbContextFactory<KokishinDbContext> contextFactory) : ICourseStore
{
    public async Task AddAsync(Course course, CancellationToken cancellationToken = default)
    {
        await using var dbContext = await contextFactory.CreateDbContextAsync(cancellationToken);
        dbContext.Courses.Add(course);
        await dbContext.SaveChangesAsync(cancellationToken);
    }

    public async Task UpdateAsync(Course course, CancellationToken cancellationToken = default)
    {
        await using var dbContext = await contextFactory.CreateDbContextAsync(cancellationToken);
        dbContext.Courses.Attach(course);
        dbContext.Entry(course).Property(existing => existing.Title).IsModified = true;
        dbContext.Entry(course).Property(existing => existing.Description).IsModified = true;
        await dbContext.SaveChangesAsync(cancellationToken);
    }

    public async Task UpdateStatusAsync(Course course, CancellationToken cancellationToken = default)
    {
        await using var dbContext = await contextFactory.CreateDbContextAsync(cancellationToken);
        dbContext.Courses.Attach(course);
        dbContext.Entry(course).Property(existing => existing.Status).IsModified = true;
        await dbContext.SaveChangesAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<Course>> ListByOwnerIdAsync(
        Guid ownerId,
        CancellationToken cancellationToken = default)
    {
        await using var dbContext = await contextFactory.CreateDbContextAsync(cancellationToken);
        return await dbContext.Courses
            .AsNoTracking()
            .Where(course => course.OwnerId == ownerId)
            .ToListAsync(cancellationToken);
    }

    public async Task<Course?> FindCourseByIdAsync(Guid courseId, CancellationToken cancellationToken = default)
    {
        await using var dbContext = await contextFactory.CreateDbContextAsync(cancellationToken);
        return await dbContext.Courses
            .AsNoTracking()
            .SingleOrDefaultAsync(course => course.Id == courseId, cancellationToken);
    }

    public async Task<Course?> FindByIdAndOwnerIdAsync(
        Guid courseId,
        Guid ownerId,
        CancellationToken cancellationToken = default)
    {
        await using var dbContext = await contextFactory.CreateDbContextAsync(cancellationToken);
        return await dbContext.Courses
            .AsNoTracking()
            .SingleOrDefaultAsync(
                course => course.Id == courseId && course.OwnerId == ownerId,
                cancellationToken);
    }
}
