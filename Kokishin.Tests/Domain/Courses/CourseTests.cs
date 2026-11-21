using Kokishin.Domain.Courses;

namespace Kokishin.Tests;

public sealed class CourseTests
{
    [Fact]
    public void Course_ValidFields_AreNormalizedAndStartAsDraft()
    {
        var ownerId = Guid.NewGuid();

        var course = new Course(ownerId, "  Biology  ", "  Introductory course  ");

        Assert.NotEqual(Guid.Empty, course.Id);
        Assert.Equal(ownerId, course.OwnerId);
        Assert.Equal("Biology", course.Title);
        Assert.Equal("Introductory course", course.Description);
        Assert.Equal(CourseStatus.Draft, course.Status);
    }

    [Theory]
    [InlineData("")]
    [InlineData("  ")]
    public void Course_EmptyTitle_IsRejected(string title)
    {
        var exception = Assert.Throws<ArgumentException>(
            () => new Course(Guid.NewGuid(), title));

        Assert.Equal("title", exception.ParamName);
    }

    [Fact]
    public void Course_EmptyOwnerId_IsRejected()
    {
        var exception = Assert.Throws<ArgumentException>(
            () => new Course(Guid.Empty, "Biology"));

        Assert.Equal("ownerId", exception.ParamName);
    }

    [Fact]
    public void Course_TitleExceedsMaximumLength_IsRejected()
    {
        var exception = Assert.Throws<ArgumentException>(
            () => new Course(Guid.NewGuid(), new string('a', Course.MaxTitleLength + 1)));

        Assert.Equal("title", exception.ParamName);
    }

    [Fact]
    public void Course_DescriptionExceedsMaximumLength_IsRejected()
    {
        var exception = Assert.Throws<ArgumentException>(
            () => new Course(
                Guid.NewGuid(),
                "Biology",
                new string('a', Course.MaxDescriptionLength + 1)));

        Assert.Equal("description", exception.ParamName);
    }

    [Fact]
    public void Course_WhitespaceDescription_IsTreatedAsAbsent()
    {
        var course = new Course(Guid.NewGuid(), "Biology", " ");

        Assert.Null(course.Description);
    }

    [Fact]
    public void UpdateInformation_ValidFields_AreNormalizedWithoutChangingIdentityOrStatus()
    {
        var ownerId = Guid.NewGuid();
        var course = new Course(ownerId, "Biology");
        var courseId = course.Id;

        course.UpdateInformation("  Chemistry  ", "  A new course description.  ");

        Assert.Equal("Chemistry", course.Title);
        Assert.Equal("A new course description.", course.Description);
        Assert.Equal(courseId, course.Id);
        Assert.Equal(ownerId, course.OwnerId);
        Assert.Equal(CourseStatus.Draft, course.Status);
    }

    [Fact]
    public void UpdateInformation_InvalidDescription_DoesNotChangeCourse()
    {
        var course = new Course(Guid.NewGuid(), "Biology", "Original description.");

        var exception = Assert.Throws<ArgumentException>(() =>
            course.UpdateInformation("Chemistry", new string('a', Course.MaxDescriptionLength + 1)));

        Assert.Equal("description", exception.ParamName);
        Assert.Equal("Biology", course.Title);
        Assert.Equal("Original description.", course.Description);
    }

    [Fact]
    public void ChangePublicationStatus_DraftToPublished_UpdatesStatus()
    {
        var course = new Course(Guid.NewGuid(), "Biology");

        course.ChangePublicationStatus(CourseStatus.Published);

        Assert.Equal(CourseStatus.Published, course.Status);
    }

    [Fact]
    public void ChangePublicationStatus_PublishedToDraft_UpdatesStatus()
    {
        var course = new Course(Guid.NewGuid(), "Biology");
        course.ChangePublicationStatus(CourseStatus.Published);

        course.ChangePublicationStatus(CourseStatus.Draft);

        Assert.Equal(CourseStatus.Draft, course.Status);
    }

    [Theory]
    [InlineData(CourseStatus.Draft)]
    [InlineData(CourseStatus.Published)]
    public void ChangePublicationStatus_CurrentStatus_IsRejectedWithoutChangingCourse(CourseStatus status)
    {
        var course = new Course(Guid.NewGuid(), "Biology");
        if (status == CourseStatus.Published)
        {
            course.ChangePublicationStatus(CourseStatus.Published);
        }

        var exception = Assert.Throws<InvalidOperationException>(
            () => course.ChangePublicationStatus(status));

        Assert.Equal(status, course.Status);
        Assert.Contains("already", exception.Message);
    }

    [Fact]
    public void ChangePublicationStatus_UnsupportedStatus_IsRejectedWithoutChangingCourse()
    {
        var course = new Course(Guid.NewGuid(), "Biology");

        var exception = Assert.Throws<ArgumentOutOfRangeException>(
            () => course.ChangePublicationStatus((CourseStatus)999));

        Assert.Equal("status", exception.ParamName);
        Assert.Equal(CourseStatus.Draft, course.Status);
    }
}
