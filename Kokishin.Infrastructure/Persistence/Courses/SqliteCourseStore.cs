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
}
