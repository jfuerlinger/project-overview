namespace ProjectOverview.Api.WorkPackages;

public enum WorkPackageStatus
{
    Open,
    InProgress,
    Done,
    Discarded,
}

/// <summary>Work that belongs to exactly one project (domain term: Arbeitspaket).</summary>
public sealed class WorkPackage
{
    public Guid Id { get; private set; }
    public Guid ProjectId { get; private set; }

    /// <summary>The parent work package within the same project, or null for a top-level package.</summary>
    public Guid? ParentId { get; private set; }
    public string Title { get; private set; } = "";
    public string? Description { get; private set; }
    public int? DurationDays { get; private set; }
    public DateOnly? StartDate { get; private set; }
    public DateOnly? EndDate { get; private set; }
    public WorkPackageStatus Status { get; private set; }
    public Guid? MilestoneId { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }

    private WorkPackage() { }

    public static WorkPackage Create(
        Guid projectId, Guid? parentId, string title, string? description, int? durationDays,
        DateOnly? startDate, DateOnly? endDate) => new()
    {
        Id = Guid.NewGuid(),
        ProjectId = projectId,
        ParentId = parentId,
        Title = title,
        Description = description,
        DurationDays = durationDays,
        StartDate = startDate,
        EndDate = endDate,
        Status = WorkPackageStatus.Open,
        CreatedAt = DateTimeOffset.UtcNow,
    };

    public void Update(string title, string? description, int? durationDays, DateOnly? startDate, DateOnly? endDate)
    {
        Title = title;
        Description = description;
        DurationDays = durationDays;
        StartDate = startDate;
        EndDate = endDate;
    }

    public void SetStatus(WorkPackageStatus status) => Status = status;

    public void MoveTo(Guid? parentId) => ParentId = parentId;

    public void SetMilestone(Guid? milestoneId) => MilestoneId = milestoneId;
}
