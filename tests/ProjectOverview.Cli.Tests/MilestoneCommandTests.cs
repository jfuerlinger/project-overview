using System.Text.Json;
using ProjectOverview.Api.Tests;

namespace ProjectOverview.Cli.Tests;

/// <summary>Seam 2 smoke tests for the milestones commands and the work package assignment.</summary>
public class MilestoneCommandTests : IDisposable
{
    private readonly ApiFixture _api = new();
    private readonly HttpClient _http;

    public MilestoneCommandTests() => _http = _api.CreateClient();

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

    private async Task<string> CreateMilestone(string projectId, string title = "Netzwerk steht", string targetDate = "2026-12-01")
    {
        var result = await CliRunner.Run(_http, "milestones", "create", "--project", projectId,
            "--title", title, "--target-date", targetDate, "--json");
        Assert.Equal(0, result.ExitCode);
        return JsonDocument.Parse(result.Output).RootElement.GetProperty("id").GetString()!;
    }

    private async Task<string> CreatePackage(string projectId, string title = "Switch kaufen")
    {
        var result = await CliRunner.Run(_http, "work-packages", "create", "--project", projectId, "--title", title, "--json");
        return JsonDocument.Parse(result.Output).RootElement.GetProperty("id").GetString()!;
    }

    private async Task<JsonElement> GetMilestone(string id)
    {
        var result = await CliRunner.Run(_http, "milestones", "get", id, "--json");
        Assert.Equal(0, result.ExitCode);
        return JsonDocument.Parse(result.Output).RootElement;
    }

    private async Task<JsonElement> GetPackage(string id)
    {
        var result = await CliRunner.Run(_http, "work-packages", "get", id, "--json");
        Assert.Equal(0, result.ExitCode);
        return JsonDocument.Parse(result.Output).RootElement;
    }

    [Fact]
    public async Task Create_prints_the_new_milestone_as_json()
    {
        var projectId = await CreateProject();

        var result = await CliRunner.Run(_http, "milestones", "create", "--project", projectId,
            "--title", "Netzwerk steht", "--description", "Alles läuft", "--target-date", "2026-12-01", "--json");

        Assert.Equal(0, result.ExitCode);
        var milestone = JsonDocument.Parse(result.Output).RootElement;
        Assert.Equal("Netzwerk steht", milestone.GetProperty("title").GetString());
        Assert.Equal(projectId, milestone.GetProperty("projectId").GetString());
        Assert.Equal("2026-12-01", milestone.GetProperty("targetDate").GetString());
        Assert.False(milestone.TryGetProperty("status", out _));
    }

    [Fact]
    public async Task Create_without_a_target_date_exits_nonzero()
    {
        var projectId = await CreateProject();

        var result = await CliRunner.Run(_http, "milestones", "create", "--project", projectId, "--title", "X");

        Assert.NotEqual(0, result.ExitCode);
    }

    [Fact]
    public async Task List_shows_only_the_milestones_of_the_given_project()
    {
        var first = await CreateProject("Erstes");
        var second = await CreateProject("Zweites");
        await CreateMilestone(first, "A");
        await CreateMilestone(second, "B");

        var result = await CliRunner.Run(_http, "milestones", "list", "--project", first, "--json");

        Assert.Equal(0, result.ExitCode);
        var titles = JsonDocument.Parse(result.Output).RootElement.EnumerateArray().Select(m => m.GetProperty("title").GetString());
        Assert.Equal(["A"], titles);
    }

    [Fact]
    public async Task List_prints_a_table_for_humans_by_default()
    {
        var projectId = await CreateProject();
        await CreateMilestone(projectId, "Netzwerk steht", "2026-12-01");

        var result = await CliRunner.Run(_http, "milestones", "list", "--project", projectId);

        Assert.Equal(0, result.ExitCode);
        var lines = result.Output.Split('\n', StringSplitOptions.RemoveEmptyEntries);
        Assert.Contains("TITLE", lines[0]);
        Assert.Contains("TARGET", lines[0]);
        Assert.Contains(lines, line => line.Contains("Netzwerk steht") && line.Contains("2026-12-01"));
    }

    [Fact]
    public async Task Get_of_an_unknown_milestone_exits_1_with_status_404()
    {
        var result = await CliRunner.Run(_http, "milestones", "get", Guid.NewGuid().ToString());

        Assert.Equal(1, result.ExitCode);
        using var json = JsonDocument.Parse(result.Error);
        Assert.Equal(404, json.RootElement.GetProperty("error").GetProperty("status").GetInt32());
    }

    [Fact]
    public async Task Update_changes_only_the_given_fields()
    {
        var projectId = await CreateProject();
        var id = await CreateMilestone(projectId, "Alt", "2026-12-01");

        var result = await CliRunner.Run(_http, "milestones", "update", id, "--title", "Neu");

        Assert.Equal(0, result.ExitCode);
        var milestone = await GetMilestone(id);
        Assert.Equal("Neu", milestone.GetProperty("title").GetString());
        Assert.Equal("2026-12-01", milestone.GetProperty("targetDate").GetString());
    }

    [Fact]
    public async Task Update_can_change_the_target_date_and_clear_the_description()
    {
        var projectId = await CreateProject();
        var create = await CliRunner.Run(_http, "milestones", "create", "--project", projectId,
            "--title", "X", "--description", "Weg", "--target-date", "2026-12-01", "--json");
        var id = JsonDocument.Parse(create.Output).RootElement.GetProperty("id").GetString()!;

        var result = await CliRunner.Run(_http, "milestones", "update", id, "--description", "", "--target-date", "2027-01-31");

        Assert.Equal(0, result.ExitCode);
        var milestone = await GetMilestone(id);
        Assert.Equal(JsonValueKind.Null, milestone.GetProperty("description").ValueKind);
        Assert.Equal("2027-01-31", milestone.GetProperty("targetDate").GetString());
    }

    [Fact]
    public async Task Update_without_any_change_exits_1()
    {
        var projectId = await CreateProject();
        var id = await CreateMilestone(projectId);

        var result = await CliRunner.Run(_http, "milestones", "update", id);

        Assert.Equal(1, result.ExitCode);
        using var json = JsonDocument.Parse(result.Error);
        Assert.Equal("nothing_to_update", json.RootElement.GetProperty("error").GetProperty("code").GetString());
    }

    [Fact]
    public async Task Delete_with_yes_removes_the_milestone_and_keeps_its_work_packages()
    {
        var projectId = await CreateProject();
        var milestoneId = await CreateMilestone(projectId);
        var packageId = await CreatePackage(projectId);
        await CliRunner.Run(_http, "work-packages", "set-milestone", packageId, "--milestone", milestoneId);

        var result = await CliRunner.Run(_http, "milestones", "delete", milestoneId, "--yes");

        Assert.Equal(0, result.ExitCode);
        Assert.Equal(1, (await CliRunner.Run(_http, "milestones", "get", milestoneId)).ExitCode);
        Assert.Equal(JsonValueKind.Null, (await GetPackage(packageId)).GetProperty("milestoneId").ValueKind);
    }

    [Fact]
    public async Task Delete_without_confirmation_keeps_the_milestone()
    {
        var projectId = await CreateProject();
        var id = await CreateMilestone(projectId, "Bleibt");

        var result = await CliRunner.RunWithInput(_http, ["n"], "milestones", "delete", id);

        Assert.Equal(1, result.ExitCode);
        Assert.Contains("Bleibt", result.Error);
        Assert.Equal("Bleibt", (await GetMilestone(id)).GetProperty("title").GetString());
    }

    [Fact]
    public async Task Delete_removes_the_milestone_after_an_affirmative_answer()
    {
        var projectId = await CreateProject();
        var id = await CreateMilestone(projectId);

        var result = await CliRunner.RunWithInput(_http, ["y"], "milestones", "delete", id);

        Assert.Equal(0, result.ExitCode);
        Assert.Equal(1, (await CliRunner.Run(_http, "milestones", "get", id)).ExitCode);
    }

    // --- assignment ---

    [Fact]
    public async Task Set_milestone_assigns_and_clear_removes_the_assignment()
    {
        var projectId = await CreateProject();
        var milestoneId = await CreateMilestone(projectId);
        var packageId = await CreatePackage(projectId);

        var assign = await CliRunner.Run(_http, "work-packages", "set-milestone", packageId, "--milestone", milestoneId, "--json");

        Assert.Equal(0, assign.ExitCode);
        Assert.Equal(milestoneId, (await GetPackage(packageId)).GetProperty("milestoneId").GetString());

        var clear = await CliRunner.Run(_http, "work-packages", "set-milestone", packageId, "--clear");

        Assert.Equal(0, clear.ExitCode);
        Assert.Equal(JsonValueKind.Null, (await GetPackage(packageId)).GetProperty("milestoneId").ValueKind);
    }

    [Fact]
    public async Task Set_milestone_requires_exactly_one_of_milestone_or_clear()
    {
        var projectId = await CreateProject();
        var milestoneId = await CreateMilestone(projectId);
        var packageId = await CreatePackage(projectId);

        var neither = await CliRunner.Run(_http, "work-packages", "set-milestone", packageId);
        var both = await CliRunner.Run(_http, "work-packages", "set-milestone", packageId, "--milestone", milestoneId, "--clear");

        Assert.NotEqual(0, neither.ExitCode);
        Assert.NotEqual(0, both.ExitCode);
    }

    [Fact]
    public async Task Set_milestone_across_projects_passes_the_api_error_through()
    {
        var projectA = await CreateProject("A");
        var projectB = await CreateProject("B");
        var foreign = await CreateMilestone(projectB);
        var packageId = await CreatePackage(projectA);

        var result = await CliRunner.Run(_http, "work-packages", "set-milestone", packageId, "--milestone", foreign);

        Assert.Equal(1, result.ExitCode);
        using var json = JsonDocument.Parse(result.Error);
        Assert.True(json.RootElement.GetProperty("error").GetProperty("errors").TryGetProperty("milestoneId", out _));
    }

    [Fact]
    public async Task Work_package_table_shows_the_assigned_milestone()
    {
        var projectId = await CreateProject();
        var milestoneId = await CreateMilestone(projectId);
        var packageId = await CreatePackage(projectId, "Switch kaufen");
        await CliRunner.Run(_http, "work-packages", "set-milestone", packageId, "--milestone", milestoneId);

        var result = await CliRunner.Run(_http, "work-packages", "list", "--project", projectId);

        var lines = result.Output.Split('\n', StringSplitOptions.RemoveEmptyEntries);
        Assert.Contains("MILESTONE", lines[0]);
        Assert.Contains(lines, line => line.Contains("Switch kaufen") && line.Contains(milestoneId));
    }
}
