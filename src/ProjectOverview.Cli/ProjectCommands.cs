using System.CommandLine;
using ProjectOverview.Client;

namespace ProjectOverview.Cli;

internal static class ProjectCommands
{
    /// <summary>The CLI's JSON contract for a project; independent of the generated client's enum casing.</summary>
    private sealed record ProjectOutput(
        Guid Id, string Name, string? Description, DateOnly? StartDate, DateOnly? TargetDate, string Status)
    {
        public static ProjectOutput From(ProjectResponse project) => new(
            project.Id, project.Name, project.Description, project.StartDate, project.TargetDate,
            CliContext.StatusName(project.Status));
    }

    public static Command Build(CliContext context)
    {
        var projects = new Command("projects", "Manage projects.");
        projects.Subcommands.Add(List(context));
        projects.Subcommands.Add(Create(context));
        projects.Subcommands.Add(Get(context));
        projects.Subcommands.Add(Update(context));
        projects.Subcommands.Add(SetStatus(context));
        projects.Subcommands.Add(Delete(context));
        return projects;
    }

    private static void Print(CliContext context, ParseResult parseResult, params ProjectOutput[] projects)
    {
        if (parseResult.GetValue(context.Json))
        {
            if (projects.Length == 1) context.WriteJson(projects[0]);
            else context.WriteJson(projects);
            return;
        }

        context.WriteTable(
            ["ID", "NAME", "STATUS", "START", "TARGET"],
            projects.Select(p => new[]
            {
                p.Id.ToString(), p.Name, p.Status, CliContext.DateText(p.StartDate), CliContext.DateText(p.TargetDate),
            }));
    }

    private static Command List(CliContext context)
    {
        var command = new Command("list", "List all projects.");
        context.Cover("listProjects");
        command.SetAction((parseResult, ct) => context.Execute(parseResult, async client =>
        {
            var projects = (await client.ListProjectsAsync(ct)).Select(ProjectOutput.From).ToArray();
            if (parseResult.GetValue(context.Json)) context.WriteJson(projects);
            else Print(context, parseResult, projects);
        }));
        return command;
    }

    private static Command Create(CliContext context)
    {
        var name = new Option<string>("--name") { Description = "Project name.", Required = true };
        var description = new Option<string?>("--description") { Description = "Project description." };
        var startDate = CliContext.DateOption("--start-date", "Start date (yyyy-MM-dd).");
        var targetDate = CliContext.DateOption("--target-date", "Target date (yyyy-MM-dd).");

        var command = new Command("create", "Create a project.") { name, description, startDate, targetDate };
        context.Cover("createProject");
        command.SetAction((parseResult, ct) => context.Execute(parseResult, async client =>
        {
            var created = await client.CreateProjectAsync(new CreateProjectRequest
            {
                Name = parseResult.GetValue(name),
                Description = parseResult.GetValue(description),
                StartDate = parseResult.GetValue(startDate),
                TargetDate = parseResult.GetValue(targetDate),
            }, ct);
            Print(context, parseResult, ProjectOutput.From(created));
        }));
        return command;
    }

    private static Command Get(CliContext context)
    {
        var id = new Argument<Guid>("id") { Description = "Project id." };
        var command = new Command("get", "Show one project.") { id };
        context.Cover("getProject");
        command.SetAction((parseResult, ct) => context.Execute(parseResult, async client =>
        {
            var project = await client.GetProjectAsync(parseResult.GetValue(id), ct);
            Print(context, parseResult, ProjectOutput.From(project));
        }));
        return command;
    }

    private static Command Update(CliContext context)
    {
        var id = new Argument<Guid>("id") { Description = "Project id." };
        var name = new Option<string?>("--name") { Description = "New name." };
        var description = new Option<string?>("--description") { Description = "New description; pass \"\" to clear it." };
        var startDate = CliContext.DateOption("--start-date", "New start date (yyyy-MM-dd).");
        var targetDate = CliContext.DateOption("--target-date", "New target date (yyyy-MM-dd).");
        var clearStartDate = new Option<bool>("--clear-start-date") { Description = "Remove the start date." };
        var clearTargetDate = new Option<bool>("--clear-target-date") { Description = "Remove the target date." };

        var command = new Command("update", "Change name, description or dates of a project. Omitted fields stay as they are.")
        {
            id, name, description, startDate, targetDate, clearStartDate, clearTargetDate,
        };
        command.Validators.Add(result =>
        {
            if (result.GetResult(startDate) is { Implicit: false } && result.GetResult(clearStartDate) is { Implicit: false })
                result.AddError("--start-date and --clear-start-date cannot be combined.");
            if (result.GetResult(targetDate) is { Implicit: false } && result.GetResult(clearTargetDate) is { Implicit: false })
                result.AddError("--target-date and --clear-target-date cannot be combined.");
        });
        context.Cover("updateProject");
        command.SetAction((parseResult, ct) => context.Execute(parseResult, async client =>
        {
            bool Given(Option option) => CliContext.Given(parseResult, option);
            if (!new Option[] { name, description, startDate, targetDate, clearStartDate, clearTargetDate }.Any(Given))
                throw new CliFailure(1, "nothing_to_update", "Pass at least one option to change.");

            // The API replaces all fields, so start from the current values and overlay what was given.
            var projectId = parseResult.GetValue(id);
            var current = await client.GetProjectAsync(projectId, ct);
            var updated = await client.UpdateProjectAsync(projectId, new UpdateProjectRequest
            {
                Name = Given(name) ? parseResult.GetValue(name) : current.Name,
                Description = Given(description) ? parseResult.GetValue(description) : current.Description,
                StartDate = Given(clearStartDate) ? null : Given(startDate) ? parseResult.GetValue(startDate) : current.StartDate,
                TargetDate = Given(clearTargetDate) ? null : Given(targetDate) ? parseResult.GetValue(targetDate) : current.TargetDate,
            }, ct);
            Print(context, parseResult, ProjectOutput.From(updated));
        }));
        return command;
    }

    private static Command SetStatus(CliContext context)
    {
        var id = new Argument<Guid>("id") { Description = "Project id." };
        var status = new Argument<string>("status") { Description = "New status." };
        status.AcceptOnlyFromAmong(CliContext.StatusNames<ProjectStatus>());

        var command = new Command("set-status", "Set the status of a project.") { id, status };
        context.Cover("setProjectStatus");
        command.SetAction((parseResult, ct) => context.Execute(parseResult, async client =>
        {
            var updated = await client.SetProjectStatusAsync(parseResult.GetValue(id), new SetProjectStatusRequest
            {
                Status = Enum.Parse<ProjectStatus>(parseResult.GetValue(status)!, ignoreCase: true),
            }, ct);
            Print(context, parseResult, ProjectOutput.From(updated));
        }));
        return command;
    }

    private static Command Delete(CliContext context)
    {
        var id = new Argument<Guid>("id") { Description = "Project id." };
        var yes = new Option<bool>("--yes", "-y") { Description = "Delete without asking for confirmation." };

        var command = new Command("delete", "Delete a project and its work packages.") { id, yes };
        context.Cover("deleteProject");
        command.SetAction((parseResult, ct) => context.Execute(parseResult, async client =>
        {
            var projectId = parseResult.GetValue(id);

            // The lookup is only needed to name the project in the prompt.
            if (!parseResult.GetValue(yes))
            {
                var project = await client.GetProjectAsync(projectId, ct);
                if (!context.Confirm($"Delete project '{project.Name}' ({project.Id}) and its work packages? [y/N] "))
                    throw new CliFailure(1, "confirmation_required", "Deletion not confirmed. Pass --yes to delete without asking.");
            }

            await client.DeleteProjectAsync(projectId, ct);
            if (parseResult.GetValue(context.Json)) context.WriteJson(new { deleted = projectId });
            else context.WriteLine($"Deleted {projectId}");
        }));
        return command;
    }
}
