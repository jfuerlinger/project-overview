namespace ProjectOverview.Api.Milestones;

/// <summary>
/// An event without duration that belongs to exactly one project (domain term: Meilenstein).
/// It has no manually settable status; whether it is reached is derived from dependencies later.
/// </summary>
public sealed class Milestone
{
    public Guid Id { get; private set; }
    public Guid ProjectId { get; private set; }
    public string Title { get; private set; } = "";
    public string? Description { get; private set; }
    public DateOnly TargetDate { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }

    private Milestone() { }

    public static Milestone Create(Guid projectId, string title, string? description, DateOnly targetDate) => new()
    {
        Id = Guid.NewGuid(),
        ProjectId = projectId,
        Title = title,
        Description = description,
        TargetDate = targetDate,
        CreatedAt = DateTimeOffset.UtcNow,
    };

    public void Update(string title, string? description, DateOnly targetDate)
    {
        Title = title;
        Description = description;
        TargetDate = targetDate;
    }
}
