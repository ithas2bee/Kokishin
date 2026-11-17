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
}
