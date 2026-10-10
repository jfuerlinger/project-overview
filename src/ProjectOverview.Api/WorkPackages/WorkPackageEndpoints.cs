using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;
using ProjectOverview.Api.Projects;

namespace ProjectOverview.Api.WorkPackages;

public static class WorkPackageLimits
{
    public const int TitleMaxLength = 200;
    public const int DescriptionMaxLength = 4000;
    public const int DurationMinDays = 1;
    public const int DurationMaxDays = 3650;
}

public sealed record CreateWorkPackageRequest(
    string? Title, string? Description, int? DurationDays, DateOnly? StartDate, DateOnly? EndDate, Guid? ParentId);

/// <summary>
/// Replaces all fields; omitted optional values are cleared. Status, project and parent are unchanged.
/// A package with sub work packages must not carry its own dates (they are derived).
/// </summary>
public sealed record UpdateWorkPackageRequest(
    string? Title, string? Description, int? DurationDays, DateOnly? StartDate, DateOnly? EndDate);

public sealed record SetWorkPackageStatusRequest(WorkPackageStatus? Status);

/// <summary>Moves a package under another package of the same project; a null parent makes it a top-level package.</summary>
public sealed record SetWorkPackageParentRequest(Guid? ParentId);

/// <summary>
/// <c>status</c>, <c>startDate</c> and <c>endDate</c> are the effective values; when
/// <c>isDerived</c> is true they come from the sub work packages and cannot be set manually.
/// </summary>
public sealed record WorkPackageResponse(
    Guid Id,
    Guid ProjectId,
    Guid? ParentId,
    string Title,
    string? Description,
    int? DurationDays,
    DateOnly? StartDate,
    DateOnly? EndDate,
    WorkPackageStatus Status,
    bool IsDerived,
    Guid? MilestoneId)
{
    public static WorkPackageResponse From(WorkPackage package, EffectiveWorkPackage effective) => new(
        package.Id, package.ProjectId, package.ParentId, package.Title, package.Description,
        package.DurationDays, effective.StartDate, effective.EndDate, effective.Status, effective.IsDerived,
        package.MilestoneId);
}

public static class WorkPackageEndpoints
{
    public static IEndpointRouteBuilder MapWorkPackageEndpoints(this IEndpointRouteBuilder routes)
    {
        routes.MapGet("/projects/{projectId:guid}/work-packages", ListWorkPackages)
            .WithTags("Work packages").WithName("listWorkPackages");
        routes.MapPost("/projects/{projectId:guid}/work-packages", CreateWorkPackage)
            .WithTags("Work packages").WithName("createWorkPackage");

        var group = routes.MapGroup("/work-packages").WithTags("Work packages");
        group.MapGet("/{id:guid}", GetWorkPackage).WithName("getWorkPackage");
        group.MapPut("/{id:guid}", UpdateWorkPackage).WithName("updateWorkPackage")
            .WithDescription("Replaces title, description, duration and dates. Optional values that are omitted are cleared. Status and project are unchanged.");
        group.MapPut("/{id:guid}/status", SetWorkPackageStatus).WithName("setWorkPackageStatus")
            .WithDescription("Not allowed for a package with sub work packages: its status is derived.");
        group.MapPut("/{id:guid}/parent", SetWorkPackageParent).WithName("setWorkPackageParent");
        group.MapDelete("/{id:guid}", DeleteWorkPackage).WithName("deleteWorkPackage");

        return routes;
    }

    private static async Task<Results<Ok<WorkPackageResponse[]>, NotFound>> ListWorkPackages(
        Guid projectId, ProjectOverviewDbContext db, CancellationToken ct)
    {
        if (!await db.Projects.AnyAsync(p => p.Id == projectId, ct)) return TypedResults.NotFound();

        var packages = await LoadProjectPackages(db, projectId, ct);
        var effective = WorkPackageHierarchy.Resolve(packages);
        return TypedResults.Ok(packages.Select(p => WorkPackageResponse.From(p, effective[p.Id])).ToArray());
    }

    private static async Task<Results<Created<WorkPackageResponse>, NotFound, ValidationProblem>> CreateWorkPackage(
        Guid projectId, CreateWorkPackageRequest request, ProjectOverviewDbContext db, CancellationToken ct)
    {
        if (!await db.Projects.AnyAsync(p => p.Id == projectId, ct)) return TypedResults.NotFound();

        var errors = Validate(request.Title, request.Description, request.DurationDays, request.StartDate, request.EndDate);
        if (request.ParentId is { } parentId)
        {
            var parent = await db.WorkPackages.AsNoTracking().SingleOrDefaultAsync(p => p.Id == parentId, ct);
            if (parent is null) errors.Add("parentId", "not_found", "Parent work package does not exist.");
            else if (parent.ProjectId != projectId)
                errors.Add("parentId", "other_project", "Parent work package belongs to another project.");
        }
        if (errors.Any) return errors.ToProblem();

        var package = WorkPackage.Create(
            projectId, request.ParentId, request.Title!.Trim(), NormalizeDescription(request.Description),
            request.DurationDays, request.StartDate, request.EndDate);
        db.WorkPackages.Add(package);
        await db.SaveChangesAsync(ct);

        return TypedResults.Created($"/work-packages/{package.Id}", await ToResponse(db, package, ct));
    }

    private static async Task<Results<Ok<WorkPackageResponse>, NotFound>> GetWorkPackage(
        Guid id, ProjectOverviewDbContext db, CancellationToken ct)
    {
        var package = await db.WorkPackages.AsNoTracking().SingleOrDefaultAsync(p => p.Id == id, ct);
        return package is null ? TypedResults.NotFound() : TypedResults.Ok(await ToResponse(db, package, ct));
    }

    private static async Task<Results<Ok<WorkPackageResponse>, NotFound, ValidationProblem>> UpdateWorkPackage(
        Guid id, UpdateWorkPackageRequest request, ProjectOverviewDbContext db, CancellationToken ct)
    {
        var package = await db.WorkPackages.SingleOrDefaultAsync(p => p.Id == id, ct);
        if (package is null) return TypedResults.NotFound();

        var errors = Validate(request.Title, request.Description, request.DurationDays, request.StartDate, request.EndDate);
        if (await HasChildren(db, id, ct))
        {
            if (request.StartDate is not null)
                errors.Add("startDate", "derived", "Start date is derived from the sub work packages and cannot be set.");
            if (request.EndDate is not null)
                errors.Add("endDate", "derived", "End date is derived from the sub work packages and cannot be set.");
        }
        if (errors.Any) return errors.ToProblem();

        package.Update(
            request.Title!.Trim(), NormalizeDescription(request.Description),
            request.DurationDays, request.StartDate, request.EndDate);
        await db.SaveChangesAsync(ct);

        return TypedResults.Ok(await ToResponse(db, package, ct));
    }

    private static async Task<Results<Ok<WorkPackageResponse>, NotFound, ValidationProblem>> SetWorkPackageStatus(
        Guid id, SetWorkPackageStatusRequest request, ProjectOverviewDbContext db, CancellationToken ct)
    {
        var package = await db.WorkPackages.SingleOrDefaultAsync(p => p.Id == id, ct);
        if (package is null) return TypedResults.NotFound();

        // Undefined values never get here: the JSON converter rejects them while binding the body.
        if (request.Status is not { } status)
            return new ValidationErrors()
                .Add("status", "required", $"Status is required (one of: {string.Join(", ", EnumNames.CamelCase<WorkPackageStatus>())}).")
                .ToProblem();

        if (await HasChildren(db, id, ct))
            return new ValidationErrors()
                .Add("status", "derived", "Status is derived from the sub work packages and cannot be set.")
                .ToProblem();

        package.SetStatus(status);
        await db.SaveChangesAsync(ct);

        return TypedResults.Ok(await ToResponse(db, package, ct));
    }

    private static async Task<Results<Ok<WorkPackageResponse>, NotFound, ValidationProblem>> SetWorkPackageParent(
        Guid id, SetWorkPackageParentRequest request, ProjectOverviewDbContext db, CancellationToken ct)
    {
        var package = await db.WorkPackages.SingleOrDefaultAsync(p => p.Id == id, ct);
        if (package is null) return TypedResults.NotFound();

        if (request.ParentId is { } parentId)
        {
            var parent = await db.WorkPackages.AsNoTracking().SingleOrDefaultAsync(p => p.Id == parentId, ct);
            if (parent is null)
                return new ValidationErrors().Add("parentId", "not_found", "Parent work package does not exist.").ToProblem();
            if (parent.ProjectId != package.ProjectId)
                return new ValidationErrors()
                    .Add("parentId", "other_project", "A work package cannot be moved into another project.").ToProblem();

            // Walk up from the new parent: meeting the package itself means it would become its own descendant.
            var projectPackages = await LoadProjectPackages(db, package.ProjectId, ct);
            var parentOf = projectPackages.ToDictionary(p => p.Id, p => p.ParentId);
            for (Guid? current = parentId; current is not null; current = parentOf.GetValueOrDefault(current.Value))
            {
                if (current == id)
                    return new ValidationErrors()
                        .Add("parentId", "cycle", "A work package cannot be moved under itself or one of its sub work packages.")
                        .ToProblem();
            }
        }

        package.MoveTo(request.ParentId);
        await db.SaveChangesAsync(ct);

        return TypedResults.Ok(await ToResponse(db, package, ct));
    }

    private static async Task<Results<NoContent, NotFound>> DeleteWorkPackage(
        Guid id, ProjectOverviewDbContext db, CancellationToken ct)
    {
        var package = await db.WorkPackages.SingleOrDefaultAsync(p => p.Id == id, ct);
        if (package is null) return TypedResults.NotFound();

        db.WorkPackages.Remove(package);
        await db.SaveChangesAsync(ct);

        return TypedResults.NoContent();
    }

    private static async Task<List<WorkPackage>> LoadProjectPackages(
        ProjectOverviewDbContext db, Guid projectId, CancellationToken ct) =>
        await db.WorkPackages.AsNoTracking()
            .Where(p => p.ProjectId == projectId)
            .OrderBy(p => p.CreatedAt).ThenBy(p => p.Id)
            .ToListAsync(ct);

    private static Task<bool> HasChildren(ProjectOverviewDbContext db, Guid id, CancellationToken ct) =>
        db.WorkPackages.AnyAsync(p => p.ParentId == id, ct);

    internal static async Task<WorkPackageResponse> ToResponse(
        ProjectOverviewDbContext db, WorkPackage package, CancellationToken ct)
    {
        var effective = WorkPackageHierarchy.Resolve(await LoadProjectPackages(db, package.ProjectId, ct));
        return WorkPackageResponse.From(package, effective[package.Id]);
    }

    private static string? NormalizeDescription(string? description) =>
        string.IsNullOrWhiteSpace(description) ? null : description.Trim();

    private static ValidationErrors Validate(
        string? title, string? description, int? durationDays, DateOnly? startDate, DateOnly? endDate)
    {
        var errors = new ValidationErrors();

        if (string.IsNullOrWhiteSpace(title))
            errors.Add("title", "required", "Title is required.");
        else if (title.Trim().Length > WorkPackageLimits.TitleMaxLength)
            errors.Add("title", $"max_length:{WorkPackageLimits.TitleMaxLength}",
                $"Title must be at most {WorkPackageLimits.TitleMaxLength} characters.");

        if (description?.Trim() is { Length: > WorkPackageLimits.DescriptionMaxLength })
            errors.Add("description", $"max_length:{WorkPackageLimits.DescriptionMaxLength}",
                $"Description must be at most {WorkPackageLimits.DescriptionMaxLength} characters.");

        if (durationDays is { } days)
        {
            if (days < WorkPackageLimits.DurationMinDays)
                errors.Add("durationDays", $"min_value:{WorkPackageLimits.DurationMinDays}",
                    $"Duration must be at least {WorkPackageLimits.DurationMinDays} day.");
            else if (days > WorkPackageLimits.DurationMaxDays)
                errors.Add("durationDays", $"max_value:{WorkPackageLimits.DurationMaxDays}",
                    $"Duration must be at most {WorkPackageLimits.DurationMaxDays} days.");
        }

        if (startDate is { } start && endDate is { } end && end < start)
            errors.Add("endDate", "before_start_date", "End date must not be before the start date.");

        return errors;
    }
}
