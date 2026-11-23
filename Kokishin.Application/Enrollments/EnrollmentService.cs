using Kokishin.Application.Accounts;
using Kokishin.Application.Courses;
using Kokishin.Domain.Accounts;
using Kokishin.Domain.Enrollments;

namespace Kokishin.Application.Enrollments;

public sealed class EnrollmentService(
    IAccountLifecycleService accountLifecycleService,
    IAccountStore accountStore,
    ICourseStore courseStore,
    IEnrollmentStore enrollmentStore) : IEnrollmentService
{
    public async Task<IReadOnlyList<Enrollment>?> ListForOwnedCourseAsync(
        Guid instructorId,
        Guid courseId,
        CancellationToken cancellationToken = default)
    {
        if (await courseStore.FindByIdAndOwnerIdAsync(courseId, instructorId, cancellationToken) is null)
        {
            return null;
        }

        return await enrollmentStore.ListByCourseIdAsync(courseId, cancellationToken);
    }

    public Task<IReadOnlyList<Enrollment>> ListForLearnerAsync(
        Guid learnerAccountId,
        CancellationToken cancellationToken = default) =>
        enrollmentStore.ListByLearnerAccountIdAsync(learnerAccountId, cancellationToken);

    public Task<Enrollment?> GetForLearnerAsync(
        Guid learnerAccountId,
        Guid courseId,
        CancellationToken cancellationToken = default) =>
        enrollmentStore.FindAsync(courseId, learnerAccountId, cancellationToken);

    public async Task<EnrollLearnerResult> EnrollLearnerAsync(
        Guid instructorId,
        Guid courseId,
        Guid learnerAccountId,
        CancellationToken cancellationToken = default)
    {
        if (await accountLifecycleService.GetRoleAsync(instructorId, cancellationToken) != AccountRole.Instructor)
        {
            return new EnrollLearnerResult(EnrollLearnerOutcome.InstructorNotAuthorized);
        }

        if (!await accountLifecycleService.IsActiveAsync(instructorId, cancellationToken))
        {
            return new EnrollLearnerResult(EnrollLearnerOutcome.InstructorInactive);
        }

        var course = await courseStore.FindByIdAndOwnerIdAsync(courseId, instructorId, cancellationToken);
        if (course is null)
        {
            return new EnrollLearnerResult(EnrollLearnerOutcome.CourseNotFound);
        }

        var learner = await accountStore.FindByIdAsync(learnerAccountId, cancellationToken);
        if (learner is null)
        {
            return new EnrollLearnerResult(EnrollLearnerOutcome.LearnerNotFound);
        }

        if (learner.Role != AccountRole.Learner || learner.Status != AccountStatus.Active)
        {
            return new EnrollLearnerResult(EnrollLearnerOutcome.LearnerNotEligible);
        }

        var existing = await enrollmentStore.FindAsync(courseId, learnerAccountId, cancellationToken);
        if (existing is not null)
        {
            return await HandleExistingEnrollmentAsync(existing, cancellationToken);
        }

        var enrollment = new Enrollment(courseId, learnerAccountId);
        var concurrentEnrollment = await enrollmentStore.AddIfAbsentAsync(enrollment, cancellationToken);
        if (concurrentEnrollment is null)
        {
            return new EnrollLearnerResult(EnrollLearnerOutcome.Created, enrollment);
        }

        return await HandleExistingEnrollmentAsync(concurrentEnrollment, cancellationToken);
    }

    public async Task<EnrollmentChangeResult> ChangeOwnedEnrollmentAsync(
        Guid instructorId,
        Guid courseId,
        Guid learnerAccountId,
        EnrollmentAction action,
        CancellationToken cancellationToken = default)
    {
        if (await accountLifecycleService.GetRoleAsync(instructorId, cancellationToken) != AccountRole.Instructor)
        {
            return new EnrollmentChangeResult(EnrollmentChangeOutcome.InstructorNotAuthorized);
        }

        if (!await accountLifecycleService.IsActiveAsync(instructorId, cancellationToken))
        {
            return new EnrollmentChangeResult(EnrollmentChangeOutcome.InstructorInactive);
        }

        if (await courseStore.FindByIdAndOwnerIdAsync(courseId, instructorId, cancellationToken) is null)
        {
            return new EnrollmentChangeResult(EnrollmentChangeOutcome.CourseNotFound);
        }

        var enrollment = await enrollmentStore.FindAsync(courseId, learnerAccountId, cancellationToken);
        if (enrollment is null)
        {
            return new EnrollmentChangeResult(EnrollmentChangeOutcome.EnrollmentNotFound);
        }

        try
        {
            switch (action)
            {
                case EnrollmentAction.Suspend:
                    enrollment.Suspend();
                    break;
                case EnrollmentAction.Reactivate:
                    enrollment.Reactivate();
                    break;
                case EnrollmentAction.Remove:
                    enrollment.Remove();
                    break;
                default:
                    throw new ArgumentOutOfRangeException(nameof(action), action, "Unsupported enrollment action.");
            }
        }
        catch (InvalidOperationException)
        {
            return new EnrollmentChangeResult(EnrollmentChangeOutcome.InvalidTransition, enrollment);
        }

        await enrollmentStore.UpdateAsync(enrollment, cancellationToken);
        return new EnrollmentChangeResult(EnrollmentChangeOutcome.Changed, enrollment);
    }

    public async Task<EnrollmentChangeResult> WithdrawOwnEnrollmentAsync(
        Guid learnerAccountId,
        Guid courseId,
        CancellationToken cancellationToken = default)
    {
        if (await accountLifecycleService.GetRoleAsync(learnerAccountId, cancellationToken) != AccountRole.Learner)
        {
            return new EnrollmentChangeResult(EnrollmentChangeOutcome.LearnerNotAuthorized);
        }

        if (!await accountLifecycleService.IsActiveAsync(learnerAccountId, cancellationToken))
        {
            return new EnrollmentChangeResult(EnrollmentChangeOutcome.LearnerInactive);
        }

        var enrollment = await enrollmentStore.FindAsync(courseId, learnerAccountId, cancellationToken);
        if (enrollment is null)
        {
            return new EnrollmentChangeResult(EnrollmentChangeOutcome.EnrollmentNotFound);
        }

        try
        {
            enrollment.Withdraw();
        }
        catch (InvalidOperationException)
        {
            return new EnrollmentChangeResult(EnrollmentChangeOutcome.InvalidTransition, enrollment);
        }

        await enrollmentStore.UpdateAsync(enrollment, cancellationToken);
        return new EnrollmentChangeResult(EnrollmentChangeOutcome.Changed, enrollment);
    }

    private async Task<EnrollLearnerResult> HandleExistingEnrollmentAsync(
        Enrollment enrollment,
        CancellationToken cancellationToken)
    {
        if (enrollment.Status == EnrollmentStatus.Active)
        {
            return new EnrollLearnerResult(EnrollLearnerOutcome.AlreadyActive, enrollment);
        }

        if (enrollment.Status == EnrollmentStatus.Suspended)
        {
            return new EnrollLearnerResult(EnrollLearnerOutcome.EnrollmentSuspended, enrollment);
        }

        enrollment.ReEnroll();
        await enrollmentStore.UpdateAsync(enrollment, cancellationToken);
        return new EnrollLearnerResult(EnrollLearnerOutcome.ReEnrolled, enrollment);
    }
}
