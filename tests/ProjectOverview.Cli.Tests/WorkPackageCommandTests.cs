using System.Text.Json;
using ProjectOverview.Api.Tests;

namespace ProjectOverview.Cli.Tests;

/// <summary>Seam 2 smoke tests for the work-packages commands.</summary>
public class WorkPackageCommandTests : IDisposable
{
    private readonly ApiFixture _api = new();
    private readonly HttpClient _http;

    public WorkPackageCommandTests() => _http = _api.CreateClient();

    public void Dispose()
    {
        _http.Dispose();
        _api.Dispose();
    }

    private async Task<string> CreateProject(string name = "Homelab")
    {
        var result = await CliRunner.Run(_http, "projects", "create", "--name", name, "--json");
        return JsonDocument.Parse(result.Output).RootElement.GetProperty("id").GetString()!;
    }

    private async Task<string> CreatePackage(string projectId, params string[] extra)
    {
        var args = new[] { "work-packages", "create", "--project", projectId, "--json" }.Concat(extra).ToArray();
        var result = await CliRunner.Run(_http, args);
        Assert.Equal(0, result.ExitCode);
        return JsonDocument.Parse(result.Output).RootElement.GetProperty("id").GetString()!;
    }

    private async Task<JsonElement> Get(string id)
    {
        var result = await CliRunner.Run(_http, "work-packages", "get", id, "--json");
        Assert.Equal(0, result.ExitCode);
        return JsonDocument.Parse(result.Output).RootElement;
    }

    [Fact]
    public async Task Create_prints_the_new_package_as_json()
    {
        var projectId = await CreateProject();

        var result = await CliRunner.Run(_http, "work-packages", "create", "--project", projectId,
            "--title", "Switch kaufen", "--description", "PoE", "--duration-days", "3",
            "--start-date", "2026-11-02", "--end-date", "2026-11-04", "--json");

        Assert.Equal(0, result.ExitCode);
        var package = JsonDocument.Parse(result.Output).RootElement;
        Assert.Equal("Switch kaufen", package.GetProperty("title").GetString());
        Assert.Equal(projectId, package.GetProperty("projectId").GetString());
        Assert.Equal(3, package.GetProperty("durationDays").GetInt32());
        Assert.Equal("2026-11-04", package.GetProperty("endDate").GetString());
        Assert.Equal("open", package.GetProperty("status").GetString());
    }

    [Fact]
    public async Task List_shows_only_the_packages_of_the_given_project()
    {
        var first = await CreateProject("Erstes");
        var second = await CreateProject("Zweites");
        await CreatePackage(first, "--title", "A1");
        await CreatePackage(second, "--title", "B1");

        var result = await CliRunner.Run(_http, "work-packages", "list", "--project", first, "--json");

        Assert.Equal(0, result.ExitCode);
        var titles = JsonDocument.Parse(result.Output).RootElement.EnumerateArray()
            .Select(p => p.GetProperty("title").GetString());
        Assert.Equal(["A1"], titles);
    }

    [Fact]
    public async Task List_prints_a_table_for_humans_by_default()
    {
        var projectId = await CreateProject();
        await CreatePackage(projectId, "--title", "Switch kaufen", "--duration-days", "3", "--start-date", "2026-11-02");

        var result = await CliRunner.Run(_http, "work-packages", "list", "--project", projectId);

        Assert.Equal(0, result.ExitCode);
        var lines = result.Output.Split('\n', StringSplitOptions.RemoveEmptyEntries);
        Assert.Contains("TITLE", lines[0]);
        Assert.Contains("STATUS", lines[0]);
        Assert.Contains(lines, line => line.Contains("Switch kaufen") && line.Contains("open") && line.Contains("2026-11-02"));
    }

    [Fact]
    public async Task List_of_an_unknown_project_exits_1_with_status_404()
    {
        var result = await CliRunner.Run(_http, "work-packages", "list", "--project", Guid.NewGuid().ToString());

        Assert.Equal(1, result.ExitCode);
        using var json = JsonDocument.Parse(result.Error);
        Assert.Equal(404, json.RootElement.GetProperty("error").GetProperty("status").GetInt32());
    }

    [Fact]
    public async Task Create_without_a_title_exits_nonzero()
    {
        var projectId = await CreateProject();

        var result = await CliRunner.Run(_http, "work-packages", "create", "--project", projectId);

        Assert.NotEqual(0, result.ExitCode);
    }

    [Fact]
    public async Task Create_with_invalid_duration_passes_the_api_error_through()
    {
        var projectId = await CreateProject();

        var result = await CliRunner.Run(_http, "work-packages", "create", "--project", projectId,
            "--title", "X", "--duration-days", "0");

        Assert.Equal(1, result.ExitCode);
        using var json = JsonDocument.Parse(result.Error);
        Assert.True(json.RootElement.GetProperty("error").GetProperty("errors").TryGetProperty("durationDays", out _));
    }

    [Fact]
    public async Task Update_changes_only_the_given_fields()
    {
        var projectId = await CreateProject();
        var id = await CreatePackage(projectId, "--title", "Alt", "--description", "Bleibt", "--duration-days", "2");

        var result = await CliRunner.Run(_http, "work-packages", "update", id, "--title", "Neu");

        Assert.Equal(0, result.ExitCode);
        var package = await Get(id);
        Assert.Equal("Neu", package.GetProperty("title").GetString());
        Assert.Equal("Bleibt", package.GetProperty("description").GetString());
        Assert.Equal(2, package.GetProperty("durationDays").GetInt32());
    }

    [Fact]
    public async Task Update_can_clear_description_duration_and_dates()
    {
        var projectId = await CreateProject();
        var id = await CreatePackage(projectId, "--title", "X", "--description", "Weg", "--duration-days", "2",
            "--start-date", "2026-11-02", "--end-date", "2026-11-03");

        var result = await CliRunner.Run(_http, "work-packages", "update", id,
            "--description", "", "--clear-duration", "--clear-start-date", "--clear-end-date");

        Assert.Equal(0, result.ExitCode);
        var package = await Get(id);
        Assert.Equal(JsonValueKind.Null, package.GetProperty("description").ValueKind);
        Assert.Equal(JsonValueKind.Null, package.GetProperty("durationDays").ValueKind);
        Assert.Equal(JsonValueKind.Null, package.GetProperty("startDate").ValueKind);
        Assert.Equal(JsonValueKind.Null, package.GetProperty("endDate").ValueKind);
    }

    [Fact]
    public async Task Update_without_any_change_exits_1()
    {
        var projectId = await CreateProject();
        var id = await CreatePackage(projectId, "--title", "X");

        var result = await CliRunner.Run(_http, "work-packages", "update", id);

        Assert.Equal(1, result.ExitCode);
        using var json = JsonDocument.Parse(result.Error);
        Assert.Equal("nothing_to_update", json.RootElement.GetProperty("error").GetProperty("code").GetString());
    }

    [Fact]
    public async Task Update_with_conflicting_options_is_rejected()
    {
        var projectId = await CreateProject();
        var id = await CreatePackage(projectId, "--title", "X");

        var result = await CliRunner.Run(_http, "work-packages", "update", id,
            "--duration-days", "3", "--clear-duration");

        Assert.NotEqual(0, result.ExitCode);
    }

    [Theory]
    [InlineData("open")]
    [InlineData("inProgress")]
    [InlineData("done")]
    [InlineData("discarded")]
    public async Task Set_status_changes_the_status(string status)
    {
        var projectId = await CreateProject();
        var id = await CreatePackage(projectId, "--title", "X");

        var result = await CliRunner.Run(_http, "work-packages", "set-status", id, status, "--json");

        Assert.Equal(0, result.ExitCode);
        Assert.Equal(status, (await Get(id)).GetProperty("status").GetString());
    }

    [Fact]
    public async Task Set_status_rejects_undefined_values()
    {
        var projectId = await CreateProject();
        var id = await CreatePackage(projectId, "--title", "X");

        var result = await CliRunner.Run(_http, "work-packages", "set-status", id, "bogus");

        Assert.NotEqual(0, result.ExitCode);
    }

    [Fact]
    public async Task Delete_with_yes_removes_the_package()
    {
        var projectId = await CreateProject();
        var id = await CreatePackage(projectId, "--title", "Weg");

        var result = await CliRunner.Run(_http, "work-packages", "delete", id, "--yes");

        Assert.Equal(0, result.ExitCode);
        Assert.Equal(1, (await CliRunner.Run(_http, "work-packages", "get", id)).ExitCode);
    }

    [Fact]
    public async Task Delete_without_confirmation_keeps_the_package()
    {
        var projectId = await CreateProject();
        var id = await CreatePackage(projectId, "--title", "Bleibt");

        var result = await CliRunner.RunWithInput(_http, ["n"], "work-packages", "delete", id);

        Assert.Equal(1, result.ExitCode);
        Assert.Contains("Bleibt", result.Error);
        Assert.Equal("Bleibt", (await Get(id)).GetProperty("title").GetString());
    }

    [Fact]
    public async Task Delete_removes_the_package_after_an_affirmative_answer()
    {
        var projectId = await CreateProject();
        var id = await CreatePackage(projectId, "--title", "Weg");

        var result = await CliRunner.RunWithInput(_http, ["y"], "work-packages", "delete", id);

        Assert.Equal(0, result.ExitCode);
        Assert.Equal(1, (await CliRunner.Run(_http, "work-packages", "get", id)).ExitCode);
    }
}
