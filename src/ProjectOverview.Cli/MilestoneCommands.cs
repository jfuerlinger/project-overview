using System.CommandLine;
using ProjectOverview.Client;

namespace ProjectOverview.Cli;

internal static class MilestoneCommands
{
    /// <summary>The CLI's JSON contract for a milestone. A milestone has no duration and no status.</summary>
    private sealed record MilestoneOutput(Guid Id, Guid ProjectId, string Title, string? Description, DateOnly TargetDate)
    {
        public static MilestoneOutput From(MilestoneResponse milestone) => new(
            milestone.Id, milestone.ProjectId, milestone.Title, milestone.Description, milestone.TargetDate);
    }

    public static Command Build(CliContext context)
    {
        var milestones = new Command("milestones", "Manage the milestones of a project.");
        milestones.Subcommands.Add(List(context));
        milestones.Subcommands.Add(Create(context));
        milestones.Subcommands.Add(Get(context));
        milestones.Subcommands.Add(Update(context));
        milestones.Subcommands.Add(Delete(context));
        return milestones;
    }

    private static void Print(CliContext context, ParseResult parseResult, MilestoneOutput[] milestones, bool asList)
    {
        if (parseResult.GetValue(context.Json))
        {
            if (asList) context.WriteJson(milestones);
            else context.WriteJson(milestones[0]);
            return;
        }

        context.WriteTable(
            ["ID", "TITLE", "TARGET"],
            milestones.Select(m => new[] { m.Id.ToString(), m.Title, CliContext.DateText(m.TargetDate) }));
    }

    private static Command List(CliContext context)
    {
        var project = new Option<Guid>("--project") { Description = "Project id.", Required = true };
        var command = new Command("list", "List the milestones of a project, earliest target date first.") { project };
        context.Cover("listMilestones");
        command.SetAction((parseResult, ct) => context.Execute(parseResult, async client =>
        {
            var milestones = (await client.ListMilestonesAsync(parseResult.GetValue(project), ct))
                .Select(MilestoneOutput.From).ToArray();
            Print(context, parseResult, milestones, asList: true);
        }));
        return command;
    }

    private static Command Create(CliContext context)
    {
        var project = new Option<Guid>("--project") { Description = "Project id.", Required = true };
        var title = new Option<string>("--title") { Description = "Milestone title.", Required = true };
        var description = new Option<string?>("--description") { Description = "Description." };
        var targetDate = CliContext.DateOption("--target-date", "Target date (yyyy-MM-dd).");
        targetDate.Required = true;

        var command = new Command("create", "Create a milestone in a project.") { project, title, description, targetDate };
        context.Cover("createMilestone");
        command.SetAction((parseResult, ct) => context.Execute(parseResult, async client =>
        {
            var created = await client.CreateMilestoneAsync(parseResult.GetValue(project), new CreateMilestoneRequest
            {
                Title = parseResult.GetValue(title),
                Description = parseResult.GetValue(description),
                TargetDate = parseResult.GetValue(targetDate),
            }, ct);
            Print(context, parseResult, [MilestoneOutput.From(created)], asList: false);
        }));
        return command;
    }

    private static Command Get(CliContext context)
    {
        var id = new Argument<Guid>("id") { Description = "Milestone id." };
        var command = new Command("get", "Show one milestone.") { id };
        context.Cover("getMilestone");
        command.SetAction((parseResult, ct) => context.Execute(parseResult, async client =>
        {
            var milestone = await client.GetMilestoneAsync(parseResult.GetValue(id), ct);
            Print(context, parseResult, [MilestoneOutput.From(milestone)], asList: false);
        }));
        return command;
    }

    private static Command Update(CliContext context)
    {
        var id = new Argument<Guid>("id") { Description = "Milestone id." };
        var title = new Option<string?>("--title") { Description = "New title." };
        var description = new Option<string?>("--description") { Description = "New description; pass \"\" to clear it." };
        var targetDate = CliContext.DateOption("--target-date", "New target date (yyyy-MM-dd).");

        var command = new Command("update", "Change a milestone. Omitted fields stay as they are.") { id, title, description, targetDate };
        context.Cover("updateMilestone");
        command.SetAction((parseResult, ct) => context.Execute(parseResult, async client =>
        {
            bool Given(Option option) => CliContext.Given(parseResult, option);
            if (!new Option[] { title, description, targetDate }.Any(Given))
                throw new CliFailure(1, "nothing_to_update", "Pass at least one option to change.");

            // The API replaces all fields, so start from the current values and overlay what was given.
            var milestoneId = parseResult.GetValue(id);
            var current = await client.GetMilestoneAsync(milestoneId, ct);
            var updated = await client.UpdateMilestoneAsync(milestoneId, new UpdateMilestoneRequest
            {
                Title = Given(title) ? parseResult.GetValue(title) : current.Title,
                Description = Given(description) ? parseResult.GetValue(description) : current.Description,
                TargetDate = Given(targetDate) ? parseResult.GetValue(targetDate) : current.TargetDate,
            }, ct);
            Print(context, parseResult, [MilestoneOutput.From(updated)], asList: false);
        }));
        return command;
    }

    private static Command Delete(CliContext context)
    {
        var id = new Argument<Guid>("id") { Description = "Milestone id." };
        var yes = new Option<bool>("--yes", "-y") { Description = "Delete without asking for confirmation." };

        var command = new Command("delete", "Delete a milestone. Its work packages stay and become unassigned.") { id, yes };
        context.Cover("deleteMilestone");
        command.SetAction((parseResult, ct) => context.Execute(parseResult, async client =>
        {
            var milestoneId = parseResult.GetValue(id);

            if (!parseResult.GetValue(yes))
            {
                var milestone = await client.GetMilestoneAsync(milestoneId, ct);
                if (!context.Confirm($"Delete milestone '{milestone.Title}' ({milestone.Id})? [y/N] "))
                    throw new CliFailure(1, "confirmation_required", "Deletion not confirmed. Pass --yes to delete without asking.");
            }

            await client.DeleteMilestoneAsync(milestoneId, ct);
            if (parseResult.GetValue(context.Json)) context.WriteJson(new { deleted = milestoneId });
            else context.WriteLine($"Deleted {milestoneId}");
        }));
        return command;
    }
}
