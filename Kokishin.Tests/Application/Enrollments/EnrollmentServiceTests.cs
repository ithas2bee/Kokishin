using Kokishin.Application.Accounts;
using Kokishin.Application.Courses;
using Kokishin.Application.Enrollments;
using Kokishin.Domain.Accounts;
using Kokishin.Domain.Courses;
using Kokishin.Domain.Enrollments;

namespace Kokishin.Tests;

public sealed class EnrollmentServiceTests
{
    [Fact]
    public async Task EnrollLearnerAsync_ActiveOwnedAccounts_CreatesActiveEnrollment()
    {
        var store = CreateStore();
        var service = CreateService(store);

        var result = await service.EnrollLearnerAsync(
            store.Instructor.Id,
            store.Course!.Id,
            store.Learner.Id);

        Assert.Equal(EnrollLearnerOutcome.Created, result.Outcome);
        Assert.Equal(EnrollmentStatus.Active, result.Enrollment!.Status);
        Assert.Equal(store.Course.Id, result.Enrollment.CourseId);
        Assert.Equal(store.Learner.Id, result.Enrollment.LearnerAccountId);
        Assert.Same(result.Enrollment, store.Enrollments.Single());
        Assert.Equal(AccountRole.Learner, store.Learner.Role);
    }

    [Fact]
    public async Task EnrollLearnerAsync_AlreadyActiveEnrollment_IsIdempotent()
    {
        var store = CreateStore();
        var enrollment = new Enrollment(store.Course!.Id, store.Learner.Id);
        store.Enrollments.Add(enrollment);
        var service = CreateService(store);

        var result = await service.EnrollLearnerAsync(store.Instructor.Id, store.Course.Id, store.Learner.Id);

        Assert.Equal(EnrollLearnerOutcome.AlreadyActive, result.Outcome);
        Assert.Same(enrollment, result.Enrollment);
        Assert.Single(store.Enrollments);
        Assert.Null(store.UpdatedEnrollment);
    }

    [Theory]
    [InlineData(EnrollmentStatus.Withdrawn)]
    [InlineData(EnrollmentStatus.Removed)]
    public async Task EnrollLearnerAsync_WithdrawnOrRemovedEnrollment_ReEnrollsSameRecord(
        EnrollmentStatus status)
    {
        var store = CreateStore();
        var enrollment = new Enrollment(store.Course!.Id, store.Learner.Id);
        if (status == EnrollmentStatus.Withdrawn)
        {
            enrollment.Withdraw();
        }
        else
        {
            enrollment.Remove();
        }

        store.Enrollments.Add(enrollment);
        var service = CreateService(store);

        var result = await service.EnrollLearnerAsync(store.Instructor.Id, store.Course.Id, store.Learner.Id);

        Assert.Equal(EnrollLearnerOutcome.ReEnrolled, result.Outcome);
        Assert.Same(enrollment, result.Enrollment);
        Assert.Same(enrollment, store.UpdatedEnrollment);
        Assert.Equal(EnrollmentStatus.Active, enrollment.Status);
        Assert.Single(store.Enrollments);
    }

    [Fact]
    public async Task EnrollLearnerAsync_SuspendedEnrollment_ReturnsConflictOutcomeWithoutChangingState()
    {
        var store = CreateStore();
        var enrollment = new Enrollment(store.Course!.Id, store.Learner.Id);
        enrollment.Suspend();
        store.Enrollments.Add(enrollment);
        var service = CreateService(store);

        var result = await service.EnrollLearnerAsync(store.Instructor.Id, store.Course.Id, store.Learner.Id);

        Assert.Equal(EnrollLearnerOutcome.EnrollmentSuspended, result.Outcome);
        Assert.Equal(EnrollmentStatus.Suspended, enrollment.Status);
        Assert.Null(store.UpdatedEnrollment);
    }

    [Fact]
    public async Task EnrollLearnerAsync_UnknownOrNonOwnedCourse_ReturnsNotFoundOutcome()
    {
        var store = CreateStore();
        var service = CreateService(store);

        var result = await service.EnrollLearnerAsync(
            store.Instructor.Id,
            Guid.NewGuid(),
            store.Learner.Id);

        Assert.Equal(EnrollLearnerOutcome.CourseNotFound, result.Outcome);
        Assert.Empty(store.Enrollments);
    }

    [Theory]
    [InlineData(AccountRole.Instructor, AccountStatus.Active)]
    [InlineData(AccountRole.Learner, AccountStatus.Inactive)]
    public async Task EnrollLearnerAsync_InstructorOrInactiveLearner_ReturnsNotEligibleOutcome(
        AccountRole role,
        AccountStatus status)
    {
        var store = CreateStore();
        var target = CreateAccount(role, status);
        store.Accounts[target.Id] = target;
        var service = CreateService(store);

        var result = await service.EnrollLearnerAsync(store.Instructor.Id, store.Course!.Id, target.Id);

        Assert.Equal(EnrollLearnerOutcome.LearnerNotEligible, result.Outcome);
        Assert.Empty(store.Enrollments);
        Assert.Equal(role, target.Role);
    }

    [Fact]
    public async Task EnrollLearnerAsync_InactiveInstructor_IsRejected()
    {
        var store = CreateStore();
        store.Instructor.Deactivate();
        var service = CreateService(store);

        var result = await service.EnrollLearnerAsync(store.Instructor.Id, store.Course!.Id, store.Learner.Id);

        Assert.Equal(EnrollLearnerOutcome.InstructorInactive, result.Outcome);
        Assert.Empty(store.Enrollments);
    }

    [Fact]
    public async Task ListForOwnedCourseAsync_OtherInstructorsCourse_ReturnsNotFound()
    {
        var store = CreateStore();
        var otherInstructor = CreateAccount(AccountRole.Instructor, AccountStatus.Active);
        var otherCourse = new Course(otherInstructor.Id, "Chemistry");
        store.Courses.Add(otherCourse);
        store.Enrollments.Add(new Enrollment(otherCourse.Id, store.Learner.Id));
        var service = CreateService(store);

        var result = await service.ListForOwnedCourseAsync(store.Instructor.Id, otherCourse.Id);

        Assert.Null(result);
    }

    [Fact]
    public async Task ListForLearnerAsync_ReturnsOnlyEnrollmentsForRequestedLearner()
    {
        var store = CreateStore();
        var otherLearner = CreateAccount(AccountRole.Learner, AccountStatus.Active);
        store.Enrollments.Add(new Enrollment(store.Course!.Id, store.Learner.Id));
        store.Enrollments.Add(new Enrollment(store.Course.Id, otherLearner.Id));
        var service = CreateService(store);

        var result = await service.ListForLearnerAsync(store.Learner.Id);

        Assert.Equal(store.Learner.Id, Assert.Single(result).LearnerAccountId);
    }

    [Theory]
    [InlineData(EnrollmentAction.Suspend, EnrollmentStatus.Active, EnrollmentStatus.Suspended)]
    [InlineData(EnrollmentAction.Reactivate, EnrollmentStatus.Suspended, EnrollmentStatus.Active)]
    [InlineData(EnrollmentAction.Remove, EnrollmentStatus.Active, EnrollmentStatus.Removed)]
    [InlineData(EnrollmentAction.Remove, EnrollmentStatus.Suspended, EnrollmentStatus.Removed)]
    public async Task ChangeOwnedEnrollmentAsync_PermittedTransitionsUpdateEnrollment(
        EnrollmentAction action,
        EnrollmentStatus initialStatus,
        EnrollmentStatus expectedStatus)
    {
        var store = CreateStore();
        var enrollment = CreateEnrollment(store, initialStatus);
        store.Enrollments.Add(enrollment);
        var service = CreateService(store);

        var result = await service.ChangeOwnedEnrollmentAsync(
            store.Instructor.Id,
            store.Course!.Id,
            store.Learner.Id,
            action);

        Assert.Equal(EnrollmentChangeOutcome.Changed, result.Outcome);
        Assert.Equal(expectedStatus, result.Enrollment!.Status);
        Assert.Same(enrollment, store.UpdatedEnrollment);
    }

    [Fact]
    public async Task ChangeOwnedEnrollmentAsync_RepeatedSuspendReturnsConflictWithoutUpdating()
    {
        var store = CreateStore();
        var enrollment = CreateEnrollment(store, EnrollmentStatus.Active);
        store.Enrollments.Add(enrollment);
        var service = CreateService(store);
        var first = await service.ChangeOwnedEnrollmentAsync(
            store.Instructor.Id,
            store.Course!.Id,
            store.Learner.Id,
            EnrollmentAction.Suspend);

        var repeated = await service.ChangeOwnedEnrollmentAsync(
            store.Instructor.Id,
            store.Course.Id,
            store.Learner.Id,
            EnrollmentAction.Suspend);

        Assert.Equal(EnrollmentChangeOutcome.Changed, first.Outcome);
        Assert.Equal(EnrollmentChangeOutcome.InvalidTransition, repeated.Outcome);
        Assert.Equal(EnrollmentStatus.Suspended, repeated.Enrollment!.Status);
        Assert.Equal(1, store.UpdateCount);
    }

    [Theory]
    [InlineData(EnrollmentStatus.Active)]
    [InlineData(EnrollmentStatus.Suspended)]
    public async Task WithdrawOwnEnrollmentAsync_AllowedStatesBecomeWithdrawn(EnrollmentStatus initialStatus)
    {
        var store = CreateStore();
        var enrollment = CreateEnrollment(store, initialStatus);
        store.Enrollments.Add(enrollment);
        var service = CreateService(store);

        var result = await service.WithdrawOwnEnrollmentAsync(store.Learner.Id, store.Course!.Id);

        Assert.Equal(EnrollmentChangeOutcome.Changed, result.Outcome);
        Assert.Equal(EnrollmentStatus.Withdrawn, result.Enrollment!.Status);
        Assert.Same(enrollment, store.UpdatedEnrollment);
    }

    [Fact]
    public async Task WithdrawOwnEnrollmentAsync_CannotWithdrawAnotherLearnersEnrollment()
    {
        var store = CreateStore();
        var otherLearner = CreateAccount(AccountRole.Learner, AccountStatus.Active);
        store.Accounts[otherLearner.Id] = otherLearner;
        store.Enrollments.Add(new Enrollment(store.Course!.Id, store.Learner.Id));
        var service = CreateService(store);

        var result = await service.WithdrawOwnEnrollmentAsync(otherLearner.Id, store.Course.Id);

        Assert.Equal(EnrollmentChangeOutcome.EnrollmentNotFound, result.Outcome);
        Assert.Null(store.UpdatedEnrollment);
        Assert.Equal(EnrollmentStatus.Active, store.Enrollments.Single().Status);
    }

    private static EnrollmentService CreateService(RecordingStore store) =>
        new(new AccountLifecycleService(store), store, store, store);

    private static RecordingStore CreateStore()
    {
        var instructor = CreateAccount(AccountRole.Instructor, AccountStatus.Active);
        var learner = CreateAccount(AccountRole.Learner, AccountStatus.Active);
        var store = new RecordingStore(instructor, learner)
        {
            Course = new Course(instructor.Id, "Biology")
        };
        store.Courses.Add(store.Course);
        return store;
    }

    private static Enrollment CreateEnrollment(RecordingStore store, EnrollmentStatus status)
    {
        var enrollment = new Enrollment(store.Course!.Id, store.Learner.Id);
        switch (status)
        {
            case EnrollmentStatus.Suspended:
                enrollment.Suspend();
                break;
            case EnrollmentStatus.Withdrawn:
                enrollment.Withdraw();
                break;
            case EnrollmentStatus.Removed:
                enrollment.Remove();
                break;
        }

        return enrollment;
    }

    private static Account CreateAccount(AccountRole role, AccountStatus status)
    {
        var account = new Account("https://identity.example.test/", Guid.NewGuid().ToString("N"), role);
        if (status == AccountStatus.Active)
        {
            account.Activate();
        }

        return account;
    }

    private sealed class RecordingStore(Account instructor, Account learner)
        : IAccountStore, ICourseStore, IEnrollmentStore
    {
        public Dictionary<Guid, Account> Accounts { get; } = new()
        {
            [instructor.Id] = instructor,
            [learner.Id] = learner
        };

        public Account Instructor { get; } = instructor;

        public Account Learner { get; } = learner;

        public Course? Course { get; set; }

        public List<Course> Courses { get; } = [];

        public List<Enrollment> Enrollments { get; } = [];

        public Enrollment? UpdatedEnrollment { get; private set; }

        public int UpdateCount { get; private set; }

        public Task<Account?> FindByIdAsync(Guid id, CancellationToken cancellationToken = default) =>
            Task.FromResult(Accounts.GetValueOrDefault(id));

        public Task<Account?> FindByIdentityAsync(
            string issuer,
            string subject,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(Accounts.Values.SingleOrDefault(account =>
                account.Issuer == issuer && account.Subject == subject));

        public Task AddAsync(Account account, CancellationToken cancellationToken = default)
        {
            Accounts[account.Id] = account;
            return Task.CompletedTask;
        }

        public Task<Account?> AddIfAbsentAsync(Account account, CancellationToken cancellationToken = default)
        {
            var existing = Accounts.Values.SingleOrDefault(existing =>
                existing.Issuer == account.Issuer && existing.Subject == account.Subject);
            if (existing is null)
            {
                Accounts[account.Id] = account;
            }

            return Task.FromResult(existing);
        }

        public Task UpdateAsync(Account account, CancellationToken cancellationToken = default)
        {
            Accounts[account.Id] = account;
            return Task.CompletedTask;
        }

        public Task AddAsync(Course course, CancellationToken cancellationToken = default)
        {
            Course = course;
            Courses.Add(course);
            return Task.CompletedTask;
        }

        public Task UpdateAsync(Course course, CancellationToken cancellationToken = default) =>
            Task.CompletedTask;

        public Task UpdateStatusAsync(Course course, CancellationToken cancellationToken = default) =>
            Task.CompletedTask;

        public Task<IReadOnlyList<Course>> ListByOwnerIdAsync(
            Guid ownerId,
            CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<Course>>(Courses.Where(course => course.OwnerId == ownerId).ToArray());

        public Task<Course?> FindCourseByIdAsync(
            Guid courseId,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(Courses.SingleOrDefault(course => course.Id == courseId));

        public Task<Course?> FindByIdAndOwnerIdAsync(
            Guid courseId,
            Guid ownerId,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(Courses.SingleOrDefault(course => course.Id == courseId && course.OwnerId == ownerId));

        public Task<Enrollment?> FindAsync(
            Guid courseId,
            Guid learnerAccountId,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(Enrollments.SingleOrDefault(enrollment =>
                enrollment.CourseId == courseId && enrollment.LearnerAccountId == learnerAccountId));

        public Task<IReadOnlyList<Enrollment>> ListByCourseIdAsync(
            Guid courseId,
            CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<Enrollment>>(
                Enrollments.Where(enrollment => enrollment.CourseId == courseId).ToArray());

        public Task<IReadOnlyList<Enrollment>> ListByLearnerAccountIdAsync(
            Guid learnerAccountId,
            CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<Enrollment>>(
                Enrollments.Where(enrollment => enrollment.LearnerAccountId == learnerAccountId).ToArray());

        public Task<Enrollment?> AddIfAbsentAsync(
            Enrollment enrollment,
            CancellationToken cancellationToken = default)
        {
            var existing = Enrollments.SingleOrDefault(item =>
                item.CourseId == enrollment.CourseId && item.LearnerAccountId == enrollment.LearnerAccountId);
            if (existing is null)
            {
                Enrollments.Add(enrollment);
            }

            return Task.FromResult(existing);
        }

        public Task UpdateAsync(Enrollment enrollment, CancellationToken cancellationToken = default)
        {
            UpdatedEnrollment = enrollment;
            UpdateCount++;
            return Task.CompletedTask;
        }
    }
}
