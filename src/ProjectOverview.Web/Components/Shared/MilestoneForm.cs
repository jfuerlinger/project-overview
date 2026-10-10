namespace ProjectOverview.Web.Components.Shared;

/// <summary>Editable milestone fields shared by the create and edit forms.</summary>
public sealed class MilestoneForm
{
    public string Title { get; set; } = "";
    public string? Description { get; set; }
    public DateOnly? TargetDate { get; set; }

    public void Reset()
    {
        Title = "";
        Description = null;
        TargetDate = null;
    }
}
