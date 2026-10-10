using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;
using ProjectOverview.Api.Projects;
using ProjectOverview.Api.WorkPackages;

namespace ProjectOverview.Api.Milestones;

public static class MilestoneLimits
{
    public const int TitleMaxLength = 200;
    public const int DescriptionMaxLength = 4000;
}

public sealed record CreateMilestoneRequest(string? Title, string? Description, DateOnly? TargetDate);

/// <summary>Replaces title, description and target date; an omitted description is cleared.</summary>
public sealed record UpdateMilestoneRequest(string? Title, string? Description, DateOnly? TargetDate);

/// <summary>Assigns the work package to a milestone of the same project; a null milestone removes the assignment.</summary>
public sealed record SetWorkPackageMilestoneRequest(Guid? MilestoneId);

public sealed record MilestoneResponse(Guid Id, Guid ProjectId, string Title, string? Description, DateOnly TargetDate)
{
    public static MilestoneResponse From(Milestone milestone) => new(
        milestone.Id, milestone.ProjectId, milestone.Title, milestone.Description, milestone.TargetDate);
}

public static class MilestoneEndpoints
{
    public static IEndpointRouteBuilder MapMilestoneEndpoints(this IEndpointRouteBuilder routes)
    {
        routes.MapGet("/projects/{projectId:guid}/milestones", ListMilestones)
            .WithTags("Milestones").WithName("listMilestones");
        routes.MapPost("/projects/{projectId:guid}/milestones", CreateMilestone)
            .WithTags("Milestones").WithName("createMilestone");

        var group = routes.MapGroup("/milestones").WithTags("Milestones");
        group.MapGet("/{id:guid}", GetMilestone).WithName("getMilestone");
        group.MapPut("/{id:guid}", UpdateMilestone).WithName("updateMilestone")
            .WithDescription("Replaces title, description and target date. An omitted description is cleared. Assigned work packages stay assigned.");
        group.MapDelete("/{id:guid}", DeleteMilestone).WithName("deleteMilestone")
            .WithDescription("Deletes the milestone. Work packages assigned to it stay and become unassigned.");

        routes.MapPut("/work-packages/{id:guid}/milestone", SetWorkPackageMilestone)
            .WithTags("Work packages").WithName("setWorkPackageMilestone");

        return routes;
    }

    private static async Task<Results<Ok<MilestoneResponse[]>, NotFound>> ListMilestones(
        Guid projectId, ProjectOverviewDbContext db, CancellationToken ct)
    {
        if (!await db.Projects.AnyAsync(p => p.Id == projectId, ct)) return TypedResults.NotFound();

        var milestones = await db.Milestones.AsNoTracking()
            .Where(m => m.ProjectId == projectId)
            .OrderBy(m => m.TargetDate).ThenBy(m => m.CreatedAt).ThenBy(m => m.Id)
            .ToListAsync(ct);
        return TypedResults.Ok(milestones.Select(MilestoneResponse.From).ToArray());
    }

    private static async Task<Results<Created<MilestoneResponse>, NotFound, ValidationProblem>> CreateMilestone(
        Guid projectId, CreateMilestoneRequest request, ProjectOverviewDbContext db, CancellationToken ct)
    {
        if (!await db.Projects.AnyAsync(p => p.Id == projectId, ct)) return TypedResults.NotFound();

        var errors = Validate(request.Title, request.Description, request.TargetDate);
        if (errors.Any) return errors.ToProblem();

        var milestone = Milestone.Create(
            projectId, request.Title!.Trim(), NormalizeDescription(request.Description), request.TargetDate!.Value);
        db.Milestones.Add(milestone);
        await db.SaveChangesAsync(ct);

        return TypedResults.Created($"/milestones/{milestone.Id}", MilestoneResponse.From(milestone));
    }

    private static async Task<Results<Ok<MilestoneResponse>, NotFound>> GetMilestone(
        Guid id, ProjectOverviewDbContext db, CancellationToken ct)
    {
        var milestone = await db.Milestones.AsNoTracking().SingleOrDefaultAsync(m => m.Id == id, ct);
        return milestone is null ? TypedResults.NotFound() : TypedResults.Ok(MilestoneResponse.From(milestone));
    }

    private static async Task<Results<Ok<MilestoneResponse>, NotFound, ValidationProblem>> UpdateMilestone(
        Guid id, UpdateMilestoneRequest request, ProjectOverviewDbContext db, CancellationToken ct)
    {
        var milestone = await db.Milestones.SingleOrDefaultAsync(m => m.Id == id, ct);
        if (milestone is null) return TypedResults.NotFound();

        var errors = Validate(request.Title, request.Description, request.TargetDate);
        if (errors.Any) return errors.ToProblem();

        milestone.Update(request.Title!.Trim(), NormalizeDescription(request.Description), request.TargetDate!.Value);
        await db.SaveChangesAsync(ct);

        return TypedResults.Ok(MilestoneResponse.From(milestone));
    }

    private static async Task<Results<NoContent, NotFound>> DeleteMilestone(
        Guid id, ProjectOverviewDbContext db, CancellationToken ct)
    {
        var milestone = await db.Milestones.SingleOrDefaultAsync(m => m.Id == id, ct);
        if (milestone is null) return TypedResults.NotFound();

        db.Milestones.Remove(milestone);
        await db.SaveChangesAsync(ct);

        return TypedResults.NoContent();
    }

    private static async Task<Results<Ok<WorkPackageResponse>, NotFound, ValidationProblem>> SetWorkPackageMilestone(
        Guid id, SetWorkPackageMilestoneRequest request, ProjectOverviewDbContext db, CancellationToken ct)
    {
        var package = await db.WorkPackages.SingleOrDefaultAsync(p => p.Id == id, ct);
        if (package is null) return TypedResults.NotFound();

        if (request.MilestoneId is { } milestoneId)
        {
            var milestone = await db.Milestones.AsNoTracking().SingleOrDefaultAsync(m => m.Id == milestoneId, ct);
            if (milestone is null)
                return new ValidationErrors()
                    .Add("milestoneId", "not_found", "The milestone does not exist.")
                    .ToProblem();
            if (milestone.ProjectId != package.ProjectId)
                return new ValidationErrors()
                    .Add("milestoneId", "other_project", "The milestone belongs to a different project than the work package.")
                    .ToProblem();
        }

        package.SetMilestone(request.MilestoneId);
        await db.SaveChangesAsync(ct);

        return TypedResults.Ok(await WorkPackageEndpoints.ToResponse(db, package, ct));
    }

    private static string? NormalizeDescription(string? description) =>
        string.IsNullOrWhiteSpace(description) ? null : description.Trim();

    private static ValidationErrors Validate(string? title, string? description, DateOnly? targetDate)
    {
        var errors = new ValidationErrors();

        if (string.IsNullOrWhiteSpace(title))
            errors.Add("title", "required", "Title is required.");
        else if (title.Trim().Length > MilestoneLimits.TitleMaxLength)
            errors.Add("title", $"max_length:{MilestoneLimits.TitleMaxLength}",
                $"Title must be at most {MilestoneLimits.TitleMaxLength} characters.");

        if (description?.Trim() is { Length: > MilestoneLimits.DescriptionMaxLength })
            errors.Add("description", $"max_length:{MilestoneLimits.DescriptionMaxLength}",
                $"Description must be at most {MilestoneLimits.DescriptionMaxLength} characters.");

        if (targetDate is null)
            errors.Add("targetDate", "required", "Target date is required.");

        return errors;
    }
}
