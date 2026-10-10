using ProjectOverview.Cli;

namespace ProjectOverview.Cli.Tests;

public sealed record CliResult(int ExitCode, string Output, string Error);

public static class CliRunner
{
    public static async Task<CliResult> Run(HttpClient http, params string[] args)
    {
        var output = new StringWriter();
        var error = new StringWriter();
        var environment = new CliEnvironment(
            Out: output,
            Error: error,
            GetEnvironmentVariable: _ => null,
            ConfigFilePath: null,
            CreateHttpClient: _ => http);

        var exitCode = await CliApp.RunAsync(args, environment);
        return new CliResult(exitCode, output.ToString(), error.ToString());
    }
}
