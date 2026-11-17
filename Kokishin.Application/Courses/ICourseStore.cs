using Kokishin.Domain.Courses;

namespace Kokishin.Application.Courses;

public interface ICourseStore
{
    Task AddAsync(Course course, CancellationToken cancellationToken = default);
}
