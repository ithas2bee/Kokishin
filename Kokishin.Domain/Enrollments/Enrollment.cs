namespace Kokishin.Domain.Enrollments;

public sealed class Enrollment
{
    private Enrollment()
    {
    }

    public Enrollment(Guid courseId, Guid learnerAccountId)
    {
        if (courseId == Guid.Empty)
        {
            throw new ArgumentException("Course is required.", nameof(courseId));
        }

        if (learnerAccountId == Guid.Empty)
        {
            throw new ArgumentException("Learner account is required.", nameof(learnerAccountId));
        }

        CourseId = courseId;
        LearnerAccountId = learnerAccountId;
        Status = EnrollmentStatus.Active;
    }

    public Guid CourseId { get; private set; }

    public Guid LearnerAccountId { get; private set; }

    public EnrollmentStatus Status { get; private set; }

    public void Suspend()
    {
        EnsureStatus(EnrollmentStatus.Active);
        Status = EnrollmentStatus.Suspended;
    }

    public void Reactivate()
    {
        EnsureStatus(EnrollmentStatus.Suspended);
        Status = EnrollmentStatus.Active;
    }

    public void Withdraw()
    {
        if (Status is not (EnrollmentStatus.Active or EnrollmentStatus.Suspended))
        {
            throw new InvalidOperationException($"A {Status} enrollment cannot be withdrawn.");
        }

        Status = EnrollmentStatus.Withdrawn;
    }

    public void Remove()
    {
        if (Status is not (EnrollmentStatus.Active or EnrollmentStatus.Suspended))
        {
            throw new InvalidOperationException($"A {Status} enrollment cannot be removed.");
        }

        Status = EnrollmentStatus.Removed;
    }

    public void ReEnroll()
    {
        if (Status is not (EnrollmentStatus.Withdrawn or EnrollmentStatus.Removed))
        {
            throw new InvalidOperationException($"A {Status} enrollment cannot be re-enrolled.");
        }

        Status = EnrollmentStatus.Active;
    }

    private void EnsureStatus(EnrollmentStatus expected)
    {
        if (Status != expected)
        {
            throw new InvalidOperationException($"A {Status} enrollment cannot transition to {expected}.");
        }
    }
}
