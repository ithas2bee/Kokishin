namespace Kokishin.Domain.Courses;

public sealed class Course
{
    public const int MaxTitleLength = 200;
    public const int MaxDescriptionLength = 2000;

    private Course()
    {
    }

    public Course(Guid ownerId, string title, string? description = null)
    {
        if (ownerId == Guid.Empty)
        {
            throw new ArgumentException("Course owner is required.", nameof(ownerId));
        }

        Id = Guid.NewGuid();
        OwnerId = ownerId;
        UpdateInformation(title, description);
        Status = CourseStatus.Draft;
    }

    public Guid Id { get; private set; }

    public Guid OwnerId { get; private set; }

    public string Title { get; private set; } = null!;

    public string? Description { get; private set; }

    public CourseStatus Status { get; private set; }

    public void UpdateInformation(string title, string? description)
    {
        if (string.IsNullOrWhiteSpace(title))
        {
            throw new ArgumentException("Title is required.", nameof(title));
        }

        var normalizedTitle = title.Trim();
        if (normalizedTitle.Length > MaxTitleLength)
        {
            throw new ArgumentException(
                $"Title must be {MaxTitleLength} characters or fewer.",
                nameof(title));
        }

        var normalizedDescription = string.IsNullOrWhiteSpace(description) ? null : description.Trim();
        if (normalizedDescription?.Length > MaxDescriptionLength)
        {
            throw new ArgumentException(
                $"Description must be {MaxDescriptionLength} characters or fewer.",
                nameof(description));
        }

        Title = normalizedTitle;
        Description = normalizedDescription;
    }
}
