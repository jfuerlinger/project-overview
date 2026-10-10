using System.CommandLine;
using ProjectOverview.Client;

namespace ProjectOverview.Cli;

internal static class WorkPackageCommands
{
    /// <summary>The CLI's JSON contract for a work package.</summary>
    private sealed record WorkPackageOutput(
        Guid Id, Guid ProjectId, Guid? ParentId, string Title, string? Description, int? DurationDays,
        DateOnly? StartDate, DateOnly? EndDate, string Status, bool IsDerived, Guid? MilestoneId)
    {
        public static WorkPackageOutput From(WorkPackageResponse package) => new(
            package.Id, package.ProjectId, package.ParentId, package.Title, package.Description,
            package.DurationDays, package.StartDate, package.EndDate, CliContext.StatusName(package.Status),
            package.IsDerived, package.MilestoneId);
    }

    public static Command Build(CliContext context)
    {
        var packages = new Command("work-packages", "Manage the work packages of a project.");
        packages.Subcommands.Add(List(context));
        packages.Subcommands.Add(Create(context));
        packages.Subcommands.Add(Get(context));
        packages.Subcommands.Add(Update(context));
        packages.Subcommands.Add(SetStatus(context));
        packages.Subcommands.Add(SetParent(context));
        packages.Subcommands.Add(SetMilestone(context));
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
            ["ID", "TITLE", "STATUS", "DAYS", "START", "END", "MILESTONE"],
            InHierarchyOrder(packages).Select(entry => new[]
            {
                entry.Package.Id.ToString(), new string(' ', entry.Depth * 2) + entry.Package.Title,
                entry.Package.Status, entry.Package.DurationDays?.ToString() ?? "-",
                CliContext.DateText(entry.Package.StartDate), CliContext.DateText(entry.Package.EndDate),
                entry.Package.MilestoneId?.ToString() ?? "-",
            }));
    }

    /// <summary>Depth-first order (parents before their children, siblings in the order given) with each depth.</summary>
    private static IEnumerable<(WorkPackageOutput Package, int Depth)> InHierarchyOrder(WorkPackageOutput[] packages)
    {
        var ids = packages.Select(p => p.Id).ToHashSet();
        var childrenOf = packages
            .Where(p => p.ParentId is { } parent && ids.Contains(parent))
            .ToLookup(p => p.ParentId!.Value);
        var visited = new HashSet<Guid>();

        IEnumerable<(WorkPackageOutput, int)> Walk(WorkPackageOutput package, int depth)
        {
            if (!visited.Add(package.Id)) yield break;
            yield return (package, depth);
            foreach (var child in childrenOf[package.Id])
                foreach (var entry in Walk(child, depth + 1))
                    yield return entry;
        }

        // Roots are packages without a parent in this list (a get of a single package has none).
        foreach (var root in packages.Where(p => p.ParentId is not { } parent || !ids.Contains(parent)))
            foreach (var entry in Walk(root, 0))
                yield return entry;
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
        var parent = new Option<Guid?>("--parent") { Description = "Parent work package (same project) to create a sub work package." };

        var command = new Command("create", "Create a work package (or a sub work package with --parent) in a project.")
        {
            project, title, description, duration, startDate, endDate, parent,
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
                ParentId = parseResult.GetValue(parent),
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
                // The dates of a package with sub work packages are derived; sending them back would be rejected.
                StartDate = Given(clearStartDate) ? null : Given(startDate) ? parseResult.GetValue(startDate)
                    : current.IsDerived ? null : current.StartDate,
                EndDate = Given(clearEndDate) ? null : Given(endDate) ? parseResult.GetValue(endDate)
                    : current.IsDerived ? null : current.EndDate,
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

    private static Command SetParent(CliContext context)
    {
        var id = new Argument<Guid>("id") { Description = "Work package id." };
        var parent = new Option<Guid?>("--parent") { Description = "Make it a sub work package of this package (same project)." };
        var root = new Option<bool>("--root") { Description = "Make it a top-level work package." };

        var command = new Command("set-parent", "Move a work package under another one or to the top level.") { id, parent, root };
        command.Validators.Add(result =>
        {
            var hasParent = result.GetResult(parent) is { Implicit: false };
            var hasRoot = result.GetResult(root) is { Implicit: false };
            if (hasParent == hasRoot) result.AddError("Pass exactly one of --parent <id> and --root.");
        });
        context.Cover("setWorkPackageParent");
        command.SetAction((parseResult, ct) => context.Execute(parseResult, async client =>
        {
            var updated = await client.SetWorkPackageParentAsync(parseResult.GetValue(id), new SetWorkPackageParentRequest
            {
                ParentId = parseResult.GetValue(root) ? null : parseResult.GetValue(parent),
            }, ct);
            Print(context, parseResult, [WorkPackageOutput.From(updated)], asList: false);
        }));
        return command;
    }

    private static Command SetMilestone(CliContext context)
    {
        var id = new Argument<Guid>("id") { Description = "Work package id." };
        var milestone = new Option<Guid?>("--milestone") { Description = "Milestone id (must belong to the same project)." };
        var clear = new Option<bool>("--clear") { Description = "Remove the assignment." };

        var command = new Command("set-milestone", "Assign a work package to a milestone, or remove the assignment.")
        {
            id, milestone, clear,
        };
        command.Validators.Add(result =>
        {
            var hasMilestone = result.GetResult(milestone) is { Implicit: false };
            var hasClear = result.GetResult(clear) is { Implicit: false };
            if (hasMilestone == hasClear) result.AddError("Pass exactly one of --milestone <id> or --clear.");
        });
        context.Cover("setWorkPackageMilestone");
        command.SetAction((parseResult, ct) => context.Execute(parseResult, async client =>
        {
            var updated = await client.SetWorkPackageMilestoneAsync(parseResult.GetValue(id), new SetWorkPackageMilestoneRequest
            {
                MilestoneId = parseResult.GetValue(clear) ? null : parseResult.GetValue(milestone),
            }, ct);
            Print(context, parseResult, [WorkPackageOutput.From(updated)], asList: false);
        }));
        return command;
    }

    private static Command Delete(CliContext context)
    {
        var id = new Argument<Guid>("id") { Description = "Work package id." };
        var yes = new Option<bool>("--yes", "-y") { Description = "Delete without asking for confirmation." };

        var command = new Command("delete", "Delete a work package and its sub work packages.") { id, yes };
        context.Cover("deleteWorkPackage");
        command.SetAction((parseResult, ct) => context.Execute(parseResult, async client =>
        {
            var packageId = parseResult.GetValue(id);

            if (!parseResult.GetValue(yes))
            {
                var package = await client.GetWorkPackageAsync(packageId, ct);
                if (!context.Confirm($"Delete work package '{package.Title}' ({package.Id}) and its sub work packages? [y/N] "))
                    throw new CliFailure(1, "confirmation_required", "Deletion not confirmed. Pass --yes to delete without asking.");
            }

            await client.DeleteWorkPackageAsync(packageId, ct);
            if (parseResult.GetValue(context.Json)) context.WriteJson(new { deleted = packageId });
            else context.WriteLine($"Deleted {packageId}");
        }));
        return command;
    }
}
