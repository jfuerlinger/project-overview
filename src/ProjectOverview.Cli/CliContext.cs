using System.CommandLine;
using System.Text.Json;
using ProjectOverview.Client;

namespace ProjectOverview.Cli;

/// <summary>A failure the CLI itself detects (not the API); reported like API errors.</summary>
internal sealed class CliFailure(int exitCode, string code, string message) : Exception(message)
{
    public int ExitCode { get; } = exitCode;
    public string Code { get; } = code;
}

/// <summary>
/// What the command groups share: global options, output, error mapping, API access and the list of
/// OpenAPI operations that have a command. <paramref name="environment"/> is null when the context is
/// only used to enumerate the covered operations.
/// </summary>
internal sealed class CliContext(CliEnvironment? environment)
{
    private static readonly JsonSerializerOptions JsonOutput = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true,
    };

    private readonly HashSet<string> _operations = [];

    public Option<bool> Json { get; } = new("--json") { Description = "Machine-readable JSON output.", Recursive = true };

    public Option<string?> ApiUrl { get; } = new("--api-url")
    {
        Description = $"API address (default: {ApiAddress.Default}, or ${ApiAddress.EnvironmentVariable}).",
        Recursive = true,
    };

    public IReadOnlyCollection<string> Operations => _operations;

    /// <summary>Declares that a command covers the given OpenAPI operation.</summary>
    public void Cover(string operationId) => _operations.Add(operationId);

    public static Option<DateOnly?> DateOption(string name, string description) => new(name)
    {
        Description = description,
        CustomParser = result =>
        {
            var token = result.Tokens.SingleOrDefault()?.Value;
            if (token is null) return null;
            if (DateOnly.TryParseExact(token, "yyyy-MM-dd", out var date)) return date;
            result.AddError($"'{token}' is not a date in the format yyyy-MM-dd.");
            return null;
        },
    };

    /// <summary>True when the option was actually given on the command line (not just defaulted).</summary>
    public static bool Given(ParseResult parseResult, Option option) =>
        parseResult.GetResult(option) is { Implicit: false };

    /// <summary>Runs an API call and maps failures to exit codes: 1 = API rejected, 2 = API not reachable or misconfigured.</summary>
    public async Task<int> Execute(ParseResult parseResult, Func<ProjectOverviewClient, Task> action)
    {
        var env = environment!;
        try
        {
            var address = ApiAddress.Resolve(
                parseResult.GetValue(ApiUrl), env.GetEnvironmentVariable, env.ConfigFilePath);
            var client = new ProjectOverviewClient(env.CreateHttpClient(address));
            await action(client);
            return 0;
        }
        catch (CliFailure failure)
        {
            return Fail(failure.ExitCode, new { code = failure.Code, title = failure.Message });
        }
        catch (ApiException<HttpValidationProblemDetails> exception)
        {
            var problem = exception.Result;
            return Fail(1, new
            {
                code = "api_error",
                status = exception.StatusCode,
                title = problem?.Title ?? exception.Message,
                errors = problem?.Errors,
            });
        }
        catch (ApiException exception)
        {
            return Fail(1, new { code = "api_error", status = exception.StatusCode, title = exception.Message });
        }
        catch (HttpRequestException exception)
        {
            return Fail(2, new { code = "api_unreachable", title = exception.Message });
        }
        catch (TaskCanceledException)
        {
            return Fail(2, new { code = "api_timeout", title = "The API did not respond in time." });
        }
        catch (ArgumentException exception)
        {
            return Fail(2, new { code = "invalid_api_address", title = exception.Message });
        }
    }

    private int Fail(int exitCode, object error)
    {
        environment!.Error.WriteLine(JsonSerializer.Serialize(new { error }, JsonOutput));
        return exitCode;
    }

    public void WriteJson<T>(T value) => environment!.Out.WriteLine(JsonSerializer.Serialize(value, JsonOutput));

    public void WriteLine(string text) => environment!.Out.WriteLine(text);

    public void WriteTable(string[] header, IEnumerable<string[]> rows)
    {
        var all = rows.Prepend(header).ToArray();
        var widths = header.Select((_, column) => all.Max(row => row[column].Length)).ToArray();

        foreach (var row in all)
        {
            var line = string.Join("  ", row.Select((cell, column) => cell.PadRight(widths[column])));
            environment!.Out.WriteLine(line.TrimEnd());
        }
    }

    /// <summary>Asks on stderr and reads one line from stdin; EOF counts as "no".</summary>
    public bool Confirm(string question)
    {
        var env = environment!;
        env.Error.Write(question);
        var answer = env.ReadLine()?.Trim();
        env.Error.WriteLine();
        return answer is not null && answer.ToLowerInvariant() is "y" or "yes" or "j" or "ja";
    }

    public static string DateText(DateOnly? date) => date?.ToString("yyyy-MM-dd") ?? "-";

    /// <summary>The wire name of a status (camelCase), independent of the generated enum's casing.</summary>
    public static string StatusName(Enum status) => JsonNamingPolicy.CamelCase.ConvertName(status.ToString());

    public static string[] StatusNames<TEnum>() where TEnum : struct, Enum =>
        Enum.GetNames<TEnum>().Select(name => JsonNamingPolicy.CamelCase.ConvertName(name)).ToArray();
}
