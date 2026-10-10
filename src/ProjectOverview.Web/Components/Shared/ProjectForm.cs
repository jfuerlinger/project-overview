namespace ProjectOverview.Web.Components.Shared;

/// <summary>Editable project fields shared by the create and edit forms.</summary>
public sealed class ProjectForm
{
    public string Name { get; set; } = "";
    public string? Description { get; set; }
    public DateOnly? StartDate { get; set; }
    public DateOnly? TargetDate { get; set; }

    public void Reset()
    {
        Name = "";
        Description = null;
        StartDate = null;
        TargetDate = null;
    }
}
