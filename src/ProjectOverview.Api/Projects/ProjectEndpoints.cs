using System.Text.Json;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;

namespace ProjectOverview.Api.Projects;

public static class ProjectLimits
{
    public const int NameMaxLength = 200;
    public const int DescriptionMaxLength = 4000;
}

public sealed record CreateProjectRequest(string? Name, string? Description, DateOnly? StartDate, DateOnly? TargetDate);

/// <summary>Replaces name, description and dates; omitted optional values are cleared. The status is not part of it.</summary>
public sealed record UpdateProjectRequest(string? Name, string? Description, DateOnly? StartDate, DateOnly? TargetDate);

public sealed record SetProjectStatusRequest(ProjectStatus? Status);

public static class ProjectStatusNames
{
    /// <summary>The wire names of all statuses (camelCase), in declaration order.</summary>
    public static readonly string[] All =
        Enum.GetNames<ProjectStatus>().Select(name => JsonNamingPolicy.CamelCase.ConvertName(name)).ToArray();
}

public sealed record ProjectResponse(
    Guid Id,
    string Name,
    string? Description,
    DateOnly? StartDate,
    DateOnly? TargetDate,
    ProjectStatus Status)
{
    public static ProjectResponse From(Project project) => new(
        project.Id, project.Name, project.Description, project.StartDate, project.TargetDate, project.Status);
}

public static class ProjectEndpoints
{
    public static IEndpointRouteBuilder MapProjectEndpoints(this IEndpointRouteBuilder routes)
    {
        var group = routes.MapGroup("/projects").WithTags("Projects");

        group.MapGet("/", ListProjects).WithName("listProjects");
        group.MapPost("/", CreateProject).WithName("createProject");
        group.MapGet("/{id:guid}", GetProject).WithName("getProject");
        group.MapPut("/{id:guid}", UpdateProject).WithName("updateProject")
            .WithDescription("Replaces name, description, start date and target date. Optional values that are omitted are cleared. The status is unchanged.");
        group.MapPut("/{id:guid}/status", SetProjectStatus).WithName("setProjectStatus");
        group.MapDelete("/{id:guid}", DeleteProject).WithName("deleteProject");

        return routes;
    }

    private static async Task<Ok<ProjectResponse[]>> ListProjects(ProjectOverviewDbContext db, CancellationToken ct)
    {
        var projects = await db.Projects.AsNoTracking().OrderBy(p => p.CreatedAt).ThenBy(p => p.Id).ToListAsync(ct);
        return TypedResults.Ok(projects.Select(ProjectResponse.From).ToArray());
    }

    private static async Task<Results<Created<ProjectResponse>, ValidationProblem>> CreateProject(
        CreateProjectRequest request, ProjectOverviewDbContext db, CancellationToken ct)
    {
        var errors = Validate(request.Name, request.Description, request.StartDate, request.TargetDate);
        if (errors.Count > 0) return TypedResults.ValidationProblem(errors);

        var project = Project.Create(
            request.Name!.Trim(), NormalizeDescription(request.Description), request.StartDate, request.TargetDate);
        db.Projects.Add(project);
        await db.SaveChangesAsync(ct);

        return TypedResults.Created($"/projects/{project.Id}", ProjectResponse.From(project));
    }

    private static async Task<Results<Ok<ProjectResponse>, NotFound>> GetProject(
        Guid id, ProjectOverviewDbContext db, CancellationToken ct)
    {
        var project = await db.Projects.AsNoTracking().SingleOrDefaultAsync(p => p.Id == id, ct);
        return project is null ? TypedResults.NotFound() : TypedResults.Ok(ProjectResponse.From(project));
    }

    private static async Task<Results<Ok<ProjectResponse>, NotFound, ValidationProblem>> UpdateProject(
        Guid id, UpdateProjectRequest request, ProjectOverviewDbContext db, CancellationToken ct)
    {
        var project = await db.Projects.SingleOrDefaultAsync(p => p.Id == id, ct);
        if (project is null) return TypedResults.NotFound();

        var errors = Validate(request.Name, request.Description, request.StartDate, request.TargetDate);
        if (errors.Count > 0) return TypedResults.ValidationProblem(errors);

        project.Update(request.Name!.Trim(), NormalizeDescription(request.Description), request.StartDate, request.TargetDate);
        await db.SaveChangesAsync(ct);

        return TypedResults.Ok(ProjectResponse.From(project));
    }

    private static async Task<Results<Ok<ProjectResponse>, NotFound, ValidationProblem>> SetProjectStatus(
        Guid id, SetProjectStatusRequest request, ProjectOverviewDbContext db, CancellationToken ct)
    {
        var project = await db.Projects.SingleOrDefaultAsync(p => p.Id == id, ct);
        if (project is null) return TypedResults.NotFound();

        // Undefined values never get here: the JSON converter rejects them while binding the body.
        if (request.Status is not { } status)
            return TypedResults.ValidationProblem(new Dictionary<string, string[]>
            {
                ["status"] = [$"Status is required (one of: {string.Join(", ", ProjectStatusNames.All)})."],
            });

        project.SetStatus(status);
        await db.SaveChangesAsync(ct);

        return TypedResults.Ok(ProjectResponse.From(project));
    }

    private static async Task<Results<NoContent, NotFound>> DeleteProject(
        Guid id, ProjectOverviewDbContext db, CancellationToken ct)
    {
        var project = await db.Projects.SingleOrDefaultAsync(p => p.Id == id, ct);
        if (project is null) return TypedResults.NotFound();

        db.Projects.Remove(project);
        await db.SaveChangesAsync(ct);

        return TypedResults.NoContent();
    }

    private static string? NormalizeDescription(string? description) =>
        string.IsNullOrWhiteSpace(description) ? null : description.Trim();

    private static Dictionary<string, string[]> Validate(
        string? name, string? description, DateOnly? startDate, DateOnly? targetDate)
    {
        var errors = new Dictionary<string, string[]>();

        if (string.IsNullOrWhiteSpace(name))
            errors["name"] = ["Name is required."];
        else if (name.Trim().Length > ProjectLimits.NameMaxLength)
            errors["name"] = [$"Name must be at most {ProjectLimits.NameMaxLength} characters."];

        if (description?.Trim() is { Length: > ProjectLimits.DescriptionMaxLength })
            errors["description"] = [$"Description must be at most {ProjectLimits.DescriptionMaxLength} characters."];

        if (startDate is { } start && targetDate is { } target && target < start)
            errors["targetDate"] = ["Target date must not be before the start date."];

        return errors;
    }
}
