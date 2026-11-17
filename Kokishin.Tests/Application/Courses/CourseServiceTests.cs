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

    [Fact]
    public async Task ListOwnedCoursesAsync_UsesAuthenticatedOwnerId()
    {
        var store = new RecordingCourseStore();
        var service = new CourseService(store);
        var ownerId = Guid.NewGuid();

        var courses = await service.ListOwnedCoursesAsync(ownerId);

        Assert.Equal(ownerId, store.ListedOwnerId);
        Assert.Same(store.Courses, courses);
    }

    [Fact]
    public async Task GetOwnedCourseAsync_UsesOwnerAndCourseIds()
    {
        var store = new RecordingCourseStore();
        var service = new CourseService(store);
        var ownerId = Guid.NewGuid();
        var courseId = Guid.NewGuid();

        var course = await service.GetOwnedCourseAsync(ownerId, courseId);

        Assert.Equal(ownerId, store.FoundOwnerId);
        Assert.Equal(courseId, store.FoundCourseId);
        Assert.Same(store.Course, course);
    }

    private sealed class RecordingCourseStore : ICourseStore
    {
        public Course? Course { get; private set; }

        public IReadOnlyList<Course> Courses { get; } = [];

        public Guid? ListedOwnerId { get; private set; }

        public Guid? FoundOwnerId { get; private set; }

        public Guid? FoundCourseId { get; private set; }

        public Task AddAsync(Course course, CancellationToken cancellationToken = default)
        {
            Course = course;
            return Task.CompletedTask;
        }

        public Task<IReadOnlyList<Course>> ListByOwnerIdAsync(
            Guid ownerId,
            CancellationToken cancellationToken = default)
        {
            ListedOwnerId = ownerId;
            return Task.FromResult(Courses);
        }

        public Task<Course?> FindByIdAndOwnerIdAsync(
            Guid courseId,
            Guid ownerId,
            CancellationToken cancellationToken = default)
        {
            FoundCourseId = courseId;
            FoundOwnerId = ownerId;
            return Task.FromResult(Course);
        }
    }
}
