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

    [Fact]
    public async Task UpdateOwnedCourseAsync_UpdatesAndPersistsOwnedCourse()
    {
        var ownerId = Guid.NewGuid();
        var store = new RecordingCourseStore
        {
            Course = new Course(ownerId, "Biology", "Original description.")
        };
        var service = new CourseService(store);
        var courseId = store.Course!.Id;

        var course = await service.UpdateOwnedCourseAsync(
            ownerId,
            courseId,
            "  Chemistry  ",
            "  Updated description.  ");

        Assert.Same(store.Course, course);
        Assert.Same(course, store.UpdatedCourse);
        Assert.Equal(ownerId, store.FoundOwnerId);
        Assert.Equal(courseId, store.FoundCourseId);
        Assert.Equal("Chemistry", course!.Title);
        Assert.Equal("Updated description.", course.Description);
        Assert.Equal(courseId, course.Id);
        Assert.Equal(ownerId, course.OwnerId);
        Assert.Equal(CourseStatus.Draft, course.Status);
    }

    [Fact]
    public async Task UpdateOwnedCourseAsync_CourseNotOwnedOrFound_ReturnsNullWithoutSaving()
    {
        var store = new RecordingCourseStore();
        var service = new CourseService(store);

        var course = await service.UpdateOwnedCourseAsync(
            Guid.NewGuid(),
            Guid.NewGuid(),
            "Chemistry");

        Assert.Null(course);
        Assert.Null(store.UpdatedCourse);
    }

    private sealed class RecordingCourseStore : ICourseStore
    {
        public Course? Course { get; set; }

        public IReadOnlyList<Course> Courses { get; } = [];

        public Course? UpdatedCourse { get; private set; }

        public Guid? ListedOwnerId { get; private set; }

        public Guid? FoundOwnerId { get; private set; }

        public Guid? FoundCourseId { get; private set; }

        public Task AddAsync(Course course, CancellationToken cancellationToken = default)
        {
            Course = course;
            return Task.CompletedTask;
        }

        public Task UpdateAsync(Course course, CancellationToken cancellationToken = default)
        {
            UpdatedCourse = course;
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
