using Kokishin.Application.Courses;
using Kokishin.Domain.Courses;

namespace Kokishin.Tests;

public sealed class CourseServiceTests
{
    [Fact]
    public async Task CreateCourseAsync_ValidCourse_PersistsAndReturnsCourse()
    {
        var store = new RecordingCourseStore();
        var service = new CourseService(store);
        var ownerId = Guid.NewGuid();

        var course = await service.CreateCourseAsync(ownerId, "Biology", "An introduction.");

        Assert.Same(course, store.Course);
        Assert.Equal(ownerId, course.OwnerId);
        Assert.Equal("Biology", course.Title);
        Assert.Equal("An introduction.", course.Description);
    }

    private sealed class RecordingCourseStore : ICourseStore
    {
        public Course? Course { get; private set; }

        public Task AddAsync(Course course, CancellationToken cancellationToken = default)
        {
            Course = course;
            return Task.CompletedTask;
        }
    }
}
