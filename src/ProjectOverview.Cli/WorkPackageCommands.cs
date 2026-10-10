using System.CommandLine;
using ProjectOverview.Client;

namespace ProjectOverview.Cli;

internal static class WorkPackageCommands
{
    /// <summary>The CLI's JSON contract for a work package.</summary>
    private sealed record WorkPackageOutput(
        Guid Id, Guid ProjectId, string Title, string? Description, int? DurationDays,
        DateOnly? StartDate, DateOnly? EndDate, string Status)
    {
        public static WorkPackageOutput From(WorkPackageResponse package) => new(
            package.Id, package.ProjectId, package.Title, package.Description, package.DurationDays,
            package.StartDate, package.EndDate, CliContext.StatusName(package.Status));
    }

    public static Command Build(CliContext context)
    {
        var packages = new Command("work-packages", "Manage the work packages of a project.");
        packages.Subcommands.Add(List(context));
        packages.Subcommands.Add(Create(context));
        packages.Subcommands.Add(Get(context));
        packages.Subcommands.Add(Update(context));
        packages.Subcommands.Add(SetStatus(context));
        packages.Subcommands.Add(Delete(context));
        return packages;
    }

    private static void Print(CliContext context, ParseResult parseResult, WorkPackageOutput[] packages, bool asList)
    {
        if (parseResult.GetValue(context.Json))
        {
            if (asList) context.WriteJson(packages);
            else context.WriteJson(packages[0]);
            return;
        }

        context.WriteTable(
            ["ID", "TITLE", "STATUS", "DAYS", "START", "END"],
            packages.Select(p => new[]
            {
                p.Id.ToString(), p.Title, p.Status, p.DurationDays?.ToString() ?? "-",
                CliContext.DateText(p.StartDate), CliContext.DateText(p.EndDate),
            }));
    }

    private static Option<int?> DurationOption(string name, string description) => new(name) { Description = description };

    private static Command List(CliContext context)
    {
        var project = new Option<Guid>("--project") { Description = "Project id.", Required = true };
        var command = new Command("list", "List the work packages of a project.") { project };
        context.Cover("listWorkPackages");
        command.SetAction((parseResult, ct) => context.Execute(parseResult, async client =>
        {
            var packages = (await client.ListWorkPackagesAsync(parseResult.GetValue(project), ct))
                .Select(WorkPackageOutput.From).ToArray();
            Print(context, parseResult, packages, asList: true);
        }));
        return command;
    }

    private static Command Create(CliContext context)
    {
        var project = new Option<Guid>("--project") { Description = "Project id.", Required = true };
        var title = new Option<string>("--title") { Description = "Work package title.", Required = true };
        var description = new Option<string?>("--description") { Description = "Description." };
        var duration = DurationOption("--duration-days", "Planned duration in days.");
        var startDate = CliContext.DateOption("--start-date", "Start date (yyyy-MM-dd).");
        var endDate = CliContext.DateOption("--end-date", "End date (yyyy-MM-dd).");

        var command = new Command("create", "Create a work package in a project.")
        {
            project, title, description, duration, startDate, endDate,
        };
        context.Cover("createWorkPackage");
        command.SetAction((parseResult, ct) => context.Execute(parseResult, async client =>
        {
            var created = await client.CreateWorkPackageAsync(parseResult.GetValue(project), new CreateWorkPackageRequest
            {
                Title = parseResult.GetValue(title),
                Description = parseResult.GetValue(description),
                DurationDays = parseResult.GetValue(duration),
                StartDate = parseResult.GetValue(startDate),
                EndDate = parseResult.GetValue(endDate),
            }, ct);
            Print(context, parseResult, [WorkPackageOutput.From(created)], asList: false);
        }));
        return command;
    }

    private static Command Get(CliContext context)
    {
        var id = new Argument<Guid>("id") { Description = "Work package id." };
        var command = new Command("get", "Show one work package.") { id };
        context.Cover("getWorkPackage");
        command.SetAction((parseResult, ct) => context.Execute(parseResult, async client =>
        {
            var package = await client.GetWorkPackageAsync(parseResult.GetValue(id), ct);
            Print(context, parseResult, [WorkPackageOutput.From(package)], asList: false);
        }));
        return command;
    }

    private static Command Update(CliContext context)
    {
        var id = new Argument<Guid>("id") { Description = "Work package id." };
        var title = new Option<string?>("--title") { Description = "New title." };
        var description = new Option<string?>("--description") { Description = "New description; pass \"\" to clear it." };
        var duration = DurationOption("--duration-days", "New planned duration in days.");
        var startDate = CliContext.DateOption("--start-date", "New start date (yyyy-MM-dd).");
        var endDate = CliContext.DateOption("--end-date", "New end date (yyyy-MM-dd).");
        var clearDuration = new Option<bool>("--clear-duration") { Description = "Remove the duration." };
        var clearStartDate = new Option<bool>("--clear-start-date") { Description = "Remove the start date." };
        var clearEndDate = new Option<bool>("--clear-end-date") { Description = "Remove the end date." };

        var command = new Command("update", "Change a work package. Omitted fields stay as they are.")
        {
            id, title, description, duration, startDate, endDate, clearDuration, clearStartDate, clearEndDate,
        };
        command.Validators.Add(result =>
        {
            (Option Value, Option Clear, string Name)[] pairs =
            [
                (duration, clearDuration, "duration"),
                (startDate, clearStartDate, "start date"),
                (endDate, clearEndDate, "end date"),
            ];
            foreach (var (value, clear, name) in pairs)
            {
                if (result.GetResult(value) is { Implicit: false } && result.GetResult(clear) is { Implicit: false })
                    result.AddError($"--{value.Name.TrimStart('-')} and --{clear.Name.TrimStart('-')} cannot be combined ({name}).");
            }
        });
        context.Cover("updateWorkPackage");
        command.SetAction((parseResult, ct) => context.Execute(parseResult, async client =>
        {
            bool Given(Option option) => CliContext.Given(parseResult, option);
            if (!new Option[] { title, description, duration, startDate, endDate, clearDuration, clearStartDate, clearEndDate }.Any(Given))
                throw new CliFailure(1, "nothing_to_update", "Pass at least one option to change.");

            // The API replaces all fields, so start from the current values and overlay what was given.
            var packageId = parseResult.GetValue(id);
            var current = await client.GetWorkPackageAsync(packageId, ct);
            var updated = await client.UpdateWorkPackageAsync(packageId, new UpdateWorkPackageRequest
            {
                Title = Given(title) ? parseResult.GetValue(title) : current.Title,
                Description = Given(description) ? parseResult.GetValue(description) : current.Description,
                DurationDays = Given(clearDuration) ? null : Given(duration) ? parseResult.GetValue(duration) : current.DurationDays,
                StartDate = Given(clearStartDate) ? null : Given(startDate) ? parseResult.GetValue(startDate) : current.StartDate,
                EndDate = Given(clearEndDate) ? null : Given(endDate) ? parseResult.GetValue(endDate) : current.EndDate,
            }, ct);
            Print(context, parseResult, [WorkPackageOutput.From(updated)], asList: false);
        }));
        return command;
    }

    private static Command SetStatus(CliContext context)
    {
        var id = new Argument<Guid>("id") { Description = "Work package id." };
        var status = new Argument<string>("status") { Description = "New status." };
        status.AcceptOnlyFromAmong(CliContext.StatusNames<WorkPackageStatus>());

        var command = new Command("set-status", "Set the status of a work package.") { id, status };
        context.Cover("setWorkPackageStatus");
        command.SetAction((parseResult, ct) => context.Execute(parseResult, async client =>
        {
            var updated = await client.SetWorkPackageStatusAsync(parseResult.GetValue(id), new SetWorkPackageStatusRequest
            {
                Status = Enum.Parse<WorkPackageStatus>(parseResult.GetValue(status)!, ignoreCase: true),
            }, ct);
            Print(context, parseResult, [WorkPackageOutput.From(updated)], asList: false);
        }));
        return command;
    }

    private static Command Delete(CliContext context)
    {
        var id = new Argument<Guid>("id") { Description = "Work package id." };
        var yes = new Option<bool>("--yes", "-y") { Description = "Delete without asking for confirmation." };

        var command = new Command("delete", "Delete a work package.") { id, yes };
        context.Cover("deleteWorkPackage");
        command.SetAction((parseResult, ct) => context.Execute(parseResult, async client =>
        {
            var packageId = parseResult.GetValue(id);

            if (!parseResult.GetValue(yes))
            {
                var package = await client.GetWorkPackageAsync(packageId, ct);
                if (!context.Confirm($"Delete work package '{package.Title}' ({package.Id})? [y/N] "))
                    throw new CliFailure(1, "confirmation_required", "Deletion not confirmed. Pass --yes to delete without asking.");
            }

            await client.DeleteWorkPackageAsync(packageId, ct);
            if (parseResult.GetValue(context.Json)) context.WriteJson(new { deleted = packageId });
            else context.WriteLine($"Deleted {packageId}");
        }));
        return command;
    }
}
