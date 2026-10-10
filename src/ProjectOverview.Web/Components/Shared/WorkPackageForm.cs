namespace ProjectOverview.Web.Components.Shared;

/// <summary>Editable work package fields shared by the create and edit forms.</summary>
public sealed class WorkPackageForm
{
    public string Title { get; set; } = "";
    public string? Description { get; set; }
    public int? DurationDays { get; set; }
    public DateOnly? StartDate { get; set; }
    public DateOnly? EndDate { get; set; }

    public void Reset()
    {
        Title = "";
        Description = null;
        DurationDays = null;
        StartDate = null;
        EndDate = null;
    }
}
