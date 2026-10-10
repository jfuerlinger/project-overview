namespace ProjectOverview.Api.Projects;

public enum ProjectStatus
{
    Open,
    InProgress,
    Done,
    Discarded,
}

public sealed class Project
{
    public Guid Id { get; private set; }
    public string Name { get; private set; } = "";
    public string? Description { get; private set; }
    public DateOnly? StartDate { get; private set; }
    public DateOnly? TargetDate { get; private set; }
    public ProjectStatus Status { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }

    private Project() { }

    public static Project Create(string name, string? description, DateOnly? startDate, DateOnly? targetDate) => new()
    {
        Id = Guid.NewGuid(),
        Name = name,
        Description = description,
        StartDate = startDate,
        TargetDate = targetDate,
        Status = ProjectStatus.Open,
        CreatedAt = DateTimeOffset.UtcNow,
    };

    public void Update(string name, string? description, DateOnly? startDate, DateOnly? targetDate)
    {
        Name = name;
        Description = description;
        StartDate = startDate;
        TargetDate = targetDate;
    }

    public void SetStatus(ProjectStatus status) => Status = status;
}
