using ProjectOverview.Cli;

namespace ProjectOverview.Cli.Tests;

public sealed record CliResult(int ExitCode, string Output, string Error);

public static class CliRunner
{
    public static Task<CliResult> Run(HttpClient http, params string[] args) => RunWithInput(http, [], args);

    /// <summary>Runs the CLI with the given lines available on standard input (for confirmation prompts).</summary>
    public static async Task<CliResult> RunWithInput(HttpClient http, string[] input, params string[] args)
    {
        var output = new StringWriter();
        var error = new StringWriter();
        var lines = new Queue<string>(input);
        var environment = new CliEnvironment(
            Out: output,
            Error: error,
            GetEnvironmentVariable: _ => null,
            ConfigFilePath: null,
            CreateHttpClient: _ => http,
            ReadLine: () => lines.Count > 0 ? lines.Dequeue() : null);

        var exitCode = await CliApp.RunAsync(args, environment);
        return new CliResult(exitCode, output.ToString(), error.ToString());
    }
}
