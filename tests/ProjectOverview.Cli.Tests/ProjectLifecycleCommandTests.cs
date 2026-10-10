using System.Text.Json;
using ProjectOverview.Api.Tests;

namespace ProjectOverview.Cli.Tests;

/// <summary>Seam 2 smoke tests for get, update, set-status and delete.</summary>
public class ProjectLifecycleCommandTests : IDisposable
{
    private readonly ApiFixture _api = new();
    private readonly HttpClient _http;

    public ProjectLifecycleCommandTests() => _http = _api.CreateClient();

    public void Dispose()
    {
        _http.Dispose();
        _api.Dispose();
    }

    private async Task<string> CreateProject(params string[] extraArgs)
    {
        var result = await CliRunner.Run(_http, [.. new[] { "projects", "create", "--json" }, .. extraArgs]);
        Assert.Equal(0, result.ExitCode);
        return JsonDocument.Parse(result.Output).RootElement.GetProperty("id").GetString()!;
    }

    private async Task<JsonElement> Get(string id)
    {
        var result = await CliRunner.Run(_http, "projects", "get", id, "--json");
        Assert.Equal(0, result.ExitCode);
        return JsonDocument.Parse(result.Output).RootElement;
    }

    // --- get ---

    [Fact]
    public async Task Get_prints_the_project_as_json()
    {
        var id = await CreateProject("--name", "Homelab");

        var project = await Get(id);

        Assert.Equal("Homelab", project.GetProperty("name").GetString());
    }

    [Fact]
    public async Task Get_of_an_unknown_project_exits_1_with_status_404()
    {
        var result = await CliRunner.Run(_http, "projects", "get", Guid.NewGuid().ToString());

        Assert.Equal(1, result.ExitCode);
        using var json = JsonDocument.Parse(result.Error);
        Assert.Equal(404, json.RootElement.GetProperty("error").GetProperty("status").GetInt32());
    }

    // --- update ---

    [Fact]
    public async Task Update_changes_only_the_given_fields()
    {
        var id = await CreateProject("--name", "Homelab", "--description", "Alt", "--target-date", "2026-12-31");

        var result = await CliRunner.Run(_http, "projects", "update", id, "--name", "Homelab 2.0", "--json");

        Assert.Equal(0, result.ExitCode);
        var project = await Get(id);
        Assert.Equal("Homelab 2.0", project.GetProperty("name").GetString());
        Assert.Equal("Alt", project.GetProperty("description").GetString());
        Assert.Equal("2026-12-31", project.GetProperty("targetDate").GetString());
    }

    [Fact]
    public async Task Update_can_clear_description_and_dates()
    {
        var id = await CreateProject(
            "--name", "Homelab", "--description", "Alt", "--start-date", "2026-11-01", "--target-date", "2026-12-31");

        var result = await CliRunner.Run(_http, "projects", "update", id,
            "--description", "", "--clear-start-date", "--clear-target-date");

        Assert.Equal(0, result.ExitCode);
        var project = await Get(id);
        Assert.Equal(JsonValueKind.Null, project.GetProperty("description").ValueKind);
        Assert.Equal(JsonValueKind.Null, project.GetProperty("startDate").ValueKind);
        Assert.Equal(JsonValueKind.Null, project.GetProperty("targetDate").ValueKind);
    }

    [Fact]
    public async Task Update_without_any_change_exits_1()
    {
        var id = await CreateProject("--name", "Homelab");

        var result = await CliRunner.Run(_http, "projects", "update", id);

        Assert.Equal(1, result.ExitCode);
        using var json = JsonDocument.Parse(result.Error);
        Assert.Equal("nothing_to_update", json.RootElement.GetProperty("error").GetProperty("code").GetString());
    }

    [Fact]
    public async Task Update_with_conflicting_date_options_is_rejected()
    {
        var id = await CreateProject("--name", "Homelab");

        var result = await CliRunner.Run(_http, "projects", "update", id,
            "--start-date", "2026-11-01", "--clear-start-date");

        Assert.NotEqual(0, result.ExitCode);
    }

    [Fact]
    public async Task Update_passes_api_validation_errors_through()
    {
        var id = await CreateProject("--name", "Homelab");

        var result = await CliRunner.Run(_http, "projects", "update", id, "--name", "   ");

        Assert.Equal(1, result.ExitCode);
        using var json = JsonDocument.Parse(result.Error);
        Assert.True(json.RootElement.GetProperty("error").GetProperty("errors").TryGetProperty("name", out _));
    }

    // --- set-status ---

    [Theory]
    [InlineData("open")]
    [InlineData("inProgress")]
    [InlineData("done")]
    [InlineData("discarded")]
    public async Task Set_status_changes_the_status(string status)
    {
        var id = await CreateProject("--name", "Homelab");

        var result = await CliRunner.Run(_http, "projects", "set-status", id, status, "--json");

        Assert.Equal(0, result.ExitCode);
        Assert.Equal(status, JsonDocument.Parse(result.Output).RootElement.GetProperty("status").GetString());
        Assert.Equal(status, (await Get(id)).GetProperty("status").GetString());
    }

    [Fact]
    public async Task Set_status_rejects_values_that_are_not_defined()
    {
        var id = await CreateProject("--name", "Homelab");

        var result = await CliRunner.Run(_http, "projects", "set-status", id, "bogus");

        Assert.NotEqual(0, result.ExitCode);
        Assert.Equal("open", (await Get(id)).GetProperty("status").GetString());
    }

    // --- delete ---

    [Fact]
    public async Task Delete_with_yes_removes_the_project_without_asking()
    {
        var id = await CreateProject("--name", "Weg");

        var result = await CliRunner.Run(_http, "projects", "delete", id, "--yes");

        Assert.Equal(0, result.ExitCode);
        var get = await CliRunner.Run(_http, "projects", "get", id);
        Assert.Equal(1, get.ExitCode);
    }

    [Fact]
    public async Task Delete_without_confirmation_keeps_the_project_and_explains_how_to_confirm()
    {
        var id = await CreateProject("--name", "Bleibt");

        var result = await CliRunner.Run(_http, "projects", "delete", id);

        Assert.Equal(1, result.ExitCode);
        using var json = JsonDocument.Parse(ExtractJson(result.Error));
        var error = json.RootElement.GetProperty("error");
        Assert.Equal("confirmation_required", error.GetProperty("code").GetString());
        Assert.Contains("--yes", error.GetProperty("title").GetString());
        Assert.Equal("Bleibt", (await Get(id)).GetProperty("name").GetString());
    }

    [Fact]
    public async Task Delete_asks_for_confirmation_and_names_the_project()
    {
        var id = await CreateProject("--name", "Wichtig");

        var result = await CliRunner.RunWithInput(_http, ["n"], "projects", "delete", id);

        Assert.Equal(1, result.ExitCode);
        Assert.Contains("Wichtig", result.Error);
        Assert.Equal("Wichtig", (await Get(id)).GetProperty("name").GetString());
    }

    [Theory]
    [InlineData("y")]
    [InlineData("yes")]
    [InlineData("j")]
    [InlineData("ja")]
    public async Task Delete_removes_the_project_after_an_affirmative_answer(string answer)
    {
        var id = await CreateProject("--name", "Weg");

        var result = await CliRunner.RunWithInput(_http, [answer], "projects", "delete", id);

        Assert.Equal(0, result.ExitCode);
        Assert.Equal(1, (await CliRunner.Run(_http, "projects", "get", id)).ExitCode);
    }

    [Fact]
    public async Task Delete_of_an_unknown_project_exits_1_with_status_404()
    {
        var result = await CliRunner.Run(_http, "projects", "delete", Guid.NewGuid().ToString(), "--yes");

        Assert.Equal(1, result.ExitCode);
        using var json = JsonDocument.Parse(result.Error);
        Assert.Equal(404, json.RootElement.GetProperty("error").GetProperty("status").GetInt32());
    }

    private static string ExtractJson(string text) => text[text.IndexOf('{')..];
}
