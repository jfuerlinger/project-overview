namespace ProjectOverview.Cli;

/// <summary>Everything the CLI needs from the outside world, so tests can substitute it.</summary>
public sealed record CliEnvironment(
    TextWriter Out,
    TextWriter Error,
    Func<string, string?> GetEnvironmentVariable,
    string? ConfigFilePath,
    Func<Uri, HttpClient> CreateHttpClient,
    Func<string?> ReadLine)
{
    public static CliEnvironment FromProcess() => new(
        Console.Out,
        Console.Error,
        Environment.GetEnvironmentVariable,
        Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "project-overview", "config.json"),
        address => new HttpClient { BaseAddress = address, Timeout = TimeSpan.FromSeconds(30) },
        Console.In.ReadLine);
}
