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
        return store;
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

        public List<Enrollment> Enrollments { get; } = [];

        public Enrollment? UpdatedEnrollment { get; private set; }

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
            return Task.CompletedTask;
        }

        public Task UpdateAsync(Course course, CancellationToken cancellationToken = default) =>
            Task.CompletedTask;

        public Task UpdateStatusAsync(Course course, CancellationToken cancellationToken = default) =>
            Task.CompletedTask;

        public Task<IReadOnlyList<Course>> ListByOwnerIdAsync(
            Guid ownerId,
            CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<Course>>(Course is { OwnerId: var courseOwnerId } &&
                courseOwnerId == ownerId ? [Course] : []);

        public Task<Course?> FindByIdAndOwnerIdAsync(
            Guid courseId,
            Guid ownerId,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(Course is { Id: var id, OwnerId: var courseOwnerId } &&
                id == courseId && courseOwnerId == ownerId ? Course : null);

        public Task<Enrollment?> FindAsync(
            Guid courseId,
            Guid learnerAccountId,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(Enrollments.SingleOrDefault(enrollment =>
                enrollment.CourseId == courseId && enrollment.LearnerAccountId == learnerAccountId));

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
            return Task.CompletedTask;
        }
    }
}
