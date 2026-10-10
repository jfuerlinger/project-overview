using System.Text.Json;
using ProjectOverview.Api.Tests;

namespace ProjectOverview.Cli.Tests;

/// <summary>Seam 2 smoke tests: the CLI drives the real API in-process.</summary>
public class ProjectsCommandTests : IDisposable
{
    private readonly ApiFixture _api = new();
    private readonly HttpClient _http;

    public ProjectsCommandTests() => _http = _api.CreateClient();

    public void Dispose()
    {
        _http.Dispose();
        _api.Dispose();
    }

    [Fact]
    public async Task Create_prints_the_new_project_as_json()
    {
        var result = await CliRunner.Run(_http,
            "projects", "create", "--name", "Homelab umbauen", "--description", "Netzwerk",
            "--start-date", "2026-11-01", "--target-date", "2026-12-31", "--json");

        Assert.Equal(0, result.ExitCode);
        using var json = JsonDocument.Parse(result.Output);
        Assert.Equal("Homelab umbauen", json.RootElement.GetProperty("name").GetString());
        Assert.Equal("2026-11-01", json.RootElement.GetProperty("startDate").GetString());
        Assert.Equal("open", json.RootElement.GetProperty("status").GetString());
        Assert.Equal("", result.Error);
    }

    [Fact]
    public async Task List_prints_an_empty_json_array_for_a_new_database()
    {
        var result = await CliRunner.Run(_http, "projects", "list", "--json");

        Assert.Equal(0, result.ExitCode);
        using var json = JsonDocument.Parse(result.Output);
        Assert.Equal(0, json.RootElement.GetArrayLength());
    }

    [Fact]
    public async Task List_includes_created_projects_as_json()
    {
        await CliRunner.Run(_http, "projects", "create", "--name", "Auto");

        var result = await CliRunner.Run(_http, "projects", "list", "--json");

        Assert.Equal(0, result.ExitCode);
        using var json = JsonDocument.Parse(result.Output);
        Assert.Equal("Auto", json.RootElement[0].GetProperty("name").GetString());
    }

    [Fact]
    public async Task List_prints_a_table_for_humans_by_default()
    {
        await CliRunner.Run(_http, "projects", "create", "--name", "Auto", "--target-date", "2026-12-31");

        var result = await CliRunner.Run(_http, "projects", "list");

        Assert.Equal(0, result.ExitCode);
        var lines = result.Output.Split('\n', StringSplitOptions.RemoveEmptyEntries);
        Assert.Contains("ID", lines[0]);
        Assert.Contains("NAME", lines[0]);
        Assert.Contains("STATUS", lines[0]);
        Assert.Contains(lines, line => line.Contains("Auto") && line.Contains("open") && line.Contains("2026-12-31"));
    }

    [Fact]
    public async Task Api_validation_errors_exit_nonzero_with_structured_error_on_stderr()
    {
        var result = await CliRunner.Run(_http, "projects", "create", "--name", "   ");

        Assert.Equal(1, result.ExitCode);
        Assert.Equal("", result.Output);
        using var json = JsonDocument.Parse(result.Error);
        var error = json.RootElement.GetProperty("error");
        Assert.Equal(400, error.GetProperty("status").GetInt32());
        Assert.True(error.GetProperty("errors").TryGetProperty("name", out _));
    }

    [Fact]
    public async Task Missing_required_option_exits_nonzero_without_calling_the_api()
    {
        var result = await CliRunner.Run(_http, "projects", "create");

        Assert.NotEqual(0, result.ExitCode);
        Assert.NotEqual("", result.Error);
        var list = await CliRunner.Run(_http, "projects", "list", "--json");
        using var json = JsonDocument.Parse(list.Output);
        Assert.Equal(0, json.RootElement.GetArrayLength());
    }

    [Fact]
    public async Task Unreachable_api_exits_with_code_2_and_structured_error()
    {
        using var unreachable = new HttpClient { BaseAddress = new Uri("http://127.0.0.1:1") };

        var result = await CliRunner.Run(unreachable, "projects", "list");

        Assert.Equal(2, result.ExitCode);
        using var json = JsonDocument.Parse(result.Error);
        Assert.Equal("api_unreachable", json.RootElement.GetProperty("error").GetProperty("code").GetString());
    }
}
