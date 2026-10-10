using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;

namespace ProjectOverview.Api.Projects;

public static class ProjectLimits
{
    public const int NameMaxLength = 200;
    public const int DescriptionMaxLength = 4000;
}

public sealed record CreateProjectRequest(string? Name, string? Description, DateOnly? StartDate, DateOnly? TargetDate);

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
        var errors = Validate(request);
        if (errors.Count > 0) return TypedResults.ValidationProblem(errors);

        var description = string.IsNullOrWhiteSpace(request.Description) ? null : request.Description.Trim();
        var project = Project.Create(request.Name!.Trim(), description, request.StartDate, request.TargetDate);
        db.Projects.Add(project);
        await db.SaveChangesAsync(ct);

        return TypedResults.Created($"/projects/{project.Id}", ProjectResponse.From(project));
    }

    private static Dictionary<string, string[]> Validate(CreateProjectRequest request)
    {
        var errors = new Dictionary<string, string[]>();

        if (string.IsNullOrWhiteSpace(request.Name))
            errors["name"] = ["Name is required."];
        else if (request.Name.Trim().Length > ProjectLimits.NameMaxLength)
            errors["name"] = [$"Name must be at most {ProjectLimits.NameMaxLength} characters."];

        if (request.Description?.Trim() is { Length: > ProjectLimits.DescriptionMaxLength })
            errors["description"] = [$"Description must be at most {ProjectLimits.DescriptionMaxLength} characters."];

        if (request.StartDate is { } start && request.TargetDate is { } target && target < start)
            errors["targetDate"] = ["Target date must not be before the start date."];

        return errors;
    }
}
