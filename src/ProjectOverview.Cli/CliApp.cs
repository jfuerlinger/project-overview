using System.CommandLine;

namespace ProjectOverview.Cli;

/// <summary>
/// The CLI is a pure API client (ADR-0003). Every command declares the OpenAPI operation it covers,
/// which lets a test enforce that no API operation is left without a command.
/// </summary>
public static class CliApp
{
    /// <summary>The OpenAPI operation ids that have a CLI command.</summary>
    public static IReadOnlyCollection<string> CoveredOperations { get; } = Build(new CliContext(environment: null)).Operations;

    public static async Task<int> RunAsync(string[] args, CliEnvironment environment)
    {
        var context = new CliContext(environment);
        var parseResult = Build(context).Root.Parse(args);
        return await parseResult.InvokeAsync(new InvocationConfiguration
        {
            Output = environment.Out,
            Error = environment.Error,
        });
    }

    private static (RootCommand Root, IReadOnlyCollection<string> Operations) Build(CliContext context)
    {
        var root = new RootCommand("Manage projects, work packages, milestones and their dependencies.");
        root.Options.Add(context.Json);
        root.Options.Add(context.ApiUrl);
        root.Subcommands.Add(ProjectCommands.Build(context));
        root.Subcommands.Add(WorkPackageCommands.Build(context));
        root.Subcommands.Add(MilestoneCommands.Build(context));
        return (root, context.Operations);
    }
}
