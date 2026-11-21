using Kokishin.Domain.Enrollments;

namespace Kokishin.Tests;

public sealed class EnrollmentTests
{
    [Fact]
    public void Constructor_ValidCourseAndLearner_StartsActive()
    {
        var courseId = Guid.NewGuid();
        var learnerId = Guid.NewGuid();

        var enrollment = new Enrollment(courseId, learnerId);

        Assert.Equal(courseId, enrollment.CourseId);
        Assert.Equal(learnerId, enrollment.LearnerAccountId);
        Assert.Equal(EnrollmentStatus.Active, enrollment.Status);
    }

    [Fact]
    public void Constructor_EmptyCourseOrLearner_ThrowsArgumentException()
    {
        Assert.Equal(
            "courseId",
            Assert.Throws<ArgumentException>(() => new Enrollment(Guid.Empty, Guid.NewGuid())).ParamName);
        Assert.Equal(
            "learnerAccountId",
            Assert.Throws<ArgumentException>(() => new Enrollment(Guid.NewGuid(), Guid.Empty)).ParamName);
    }

    [Fact]
    public void EnrollmentLifecycle_AllowedTransitionsFollowDecision()
    {
        var suspended = new Enrollment(Guid.NewGuid(), Guid.NewGuid());
        suspended.Suspend();
        suspended.Reactivate();
        Assert.Equal(EnrollmentStatus.Active, suspended.Status);

        var withdrawn = new Enrollment(Guid.NewGuid(), Guid.NewGuid());
        withdrawn.Withdraw();
        withdrawn.ReEnroll();
        Assert.Equal(EnrollmentStatus.Active, withdrawn.Status);

        var removed = new Enrollment(Guid.NewGuid(), Guid.NewGuid());
        removed.Suspend();
        removed.Remove();
        removed.ReEnroll();
        Assert.Equal(EnrollmentStatus.Active, removed.Status);
    }

    [Fact]
    public void EnrollmentLifecycle_InvalidTransitions_ThrowInvalidOperationException()
    {
        var enrollment = new Enrollment(Guid.NewGuid(), Guid.NewGuid());

        Assert.Throws<InvalidOperationException>(enrollment.Reactivate);
        Assert.Throws<InvalidOperationException>(enrollment.ReEnroll);

        enrollment.Remove();
        Assert.Throws<InvalidOperationException>(enrollment.Remove);
        Assert.Throws<InvalidOperationException>(enrollment.Withdraw);
        Assert.Throws<InvalidOperationException>(enrollment.Reactivate);
    }
}
