namespace ProjectOverview.Web.Components.Shared;

/// <summary>Editable work package fields shared by the create and edit forms.</summary>
public sealed class WorkPackageForm
{
    public string Title { get; set; } = "";
    public Guid? ParentId { get; set; }

    /// <summary>True while the package has sub work packages; its dates are then derived and not editable.</summary>
    public bool IsDerived { get; set; }
    public string? Description { get; set; }
    public int? DurationDays { get; set; }
    public DateOnly? StartDate { get; set; }
    public DateOnly? EndDate { get; set; }

    public void Reset()
    {
        Title = "";
        ParentId = null;
        IsDerived = false;
        Description = null;
        DurationDays = null;
        StartDate = null;
        EndDate = null;
    }
}
