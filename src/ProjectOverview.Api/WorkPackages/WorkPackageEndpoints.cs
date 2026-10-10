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
    string? Title, string? Description, int? DurationDays, DateOnly? StartDate, DateOnly? EndDate);

/// <summary>Replaces all fields; omitted optional values are cleared. The status and the project are unchanged.</summary>
public sealed record UpdateWorkPackageRequest(
    string? Title, string? Description, int? DurationDays, DateOnly? StartDate, DateOnly? EndDate);

public sealed record SetWorkPackageStatusRequest(WorkPackageStatus? Status);

public sealed record WorkPackageResponse(
    Guid Id,
    Guid ProjectId,
    string Title,
    string? Description,
    int? DurationDays,
    DateOnly? StartDate,
    DateOnly? EndDate,
    WorkPackageStatus Status)
{
    public static WorkPackageResponse From(WorkPackage package) => new(
        package.Id, package.ProjectId, package.Title, package.Description,
        package.DurationDays, package.StartDate, package.EndDate, package.Status);
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
        group.MapPut("/{id:guid}/status", SetWorkPackageStatus).WithName("setWorkPackageStatus");
        group.MapDelete("/{id:guid}", DeleteWorkPackage).WithName("deleteWorkPackage");

        return routes;
    }

    private static async Task<Results<Ok<WorkPackageResponse[]>, NotFound>> ListWorkPackages(
        Guid projectId, ProjectOverviewDbContext db, CancellationToken ct)
    {
        if (!await db.Projects.AnyAsync(p => p.Id == projectId, ct)) return TypedResults.NotFound();

        var packages = await db.WorkPackages.AsNoTracking()
            .Where(p => p.ProjectId == projectId)
            .OrderBy(p => p.CreatedAt).ThenBy(p => p.Id)
            .ToListAsync(ct);
        return TypedResults.Ok(packages.Select(WorkPackageResponse.From).ToArray());
    }

    private static async Task<Results<Created<WorkPackageResponse>, NotFound, ValidationProblem>> CreateWorkPackage(
        Guid projectId, CreateWorkPackageRequest request, ProjectOverviewDbContext db, CancellationToken ct)
    {
        if (!await db.Projects.AnyAsync(p => p.Id == projectId, ct)) return TypedResults.NotFound();

        var errors = Validate(request.Title, request.Description, request.DurationDays, request.StartDate, request.EndDate);
        if (errors.Any) return errors.ToProblem();

        var package = WorkPackage.Create(
            projectId, request.Title!.Trim(), NormalizeDescription(request.Description),
            request.DurationDays, request.StartDate, request.EndDate);
        db.WorkPackages.Add(package);
        await db.SaveChangesAsync(ct);

        return TypedResults.Created($"/work-packages/{package.Id}", WorkPackageResponse.From(package));
    }

    private static async Task<Results<Ok<WorkPackageResponse>, NotFound>> GetWorkPackage(
        Guid id, ProjectOverviewDbContext db, CancellationToken ct)
    {
        var package = await db.WorkPackages.AsNoTracking().SingleOrDefaultAsync(p => p.Id == id, ct);
        return package is null ? TypedResults.NotFound() : TypedResults.Ok(WorkPackageResponse.From(package));
    }

    private static async Task<Results<Ok<WorkPackageResponse>, NotFound, ValidationProblem>> UpdateWorkPackage(
        Guid id, UpdateWorkPackageRequest request, ProjectOverviewDbContext db, CancellationToken ct)
    {
        var package = await db.WorkPackages.SingleOrDefaultAsync(p => p.Id == id, ct);
        if (package is null) return TypedResults.NotFound();

        var errors = Validate(request.Title, request.Description, request.DurationDays, request.StartDate, request.EndDate);
        if (errors.Any) return errors.ToProblem();

        package.Update(
            request.Title!.Trim(), NormalizeDescription(request.Description),
            request.DurationDays, request.StartDate, request.EndDate);
        await db.SaveChangesAsync(ct);

        return TypedResults.Ok(WorkPackageResponse.From(package));
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

        package.SetStatus(status);
        await db.SaveChangesAsync(ct);

        return TypedResults.Ok(WorkPackageResponse.From(package));
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
