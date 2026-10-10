using System.Text.Json;
using ProjectOverview.Api.Tests;

namespace ProjectOverview.Cli.Tests;

/// <summary>Seam 2 smoke tests for sub work packages in the CLI.</summary>
public class WorkPackageHierarchyCommandTests : IDisposable
{
    private readonly ApiFixture _api = new();
    private readonly HttpClient _http;

    public WorkPackageHierarchyCommandTests() => _http = _api.CreateClient();

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

    private async Task<string> CreatePackage(string projectId, string title, string? parentId = null, params string[] extra)
    {
        var args = new List<string> { "work-packages", "create", "--project", projectId, "--title", title, "--json" };
        if (parentId is not null) args.AddRange(["--parent", parentId]);
        args.AddRange(extra);
        var result = await CliRunner.Run(_http, [.. args]);
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
    public async Task Create_with_parent_prints_the_parent_id_and_marks_the_parent_as_derived()
    {
        var projectId = await CreateProject();
        var parent = await CreatePackage(projectId, "Netzwerk");

        var result = await CliRunner.Run(_http, "work-packages", "create", "--project", projectId,
            "--title", "Switch kaufen", "--parent", parent, "--json");

        Assert.Equal(0, result.ExitCode);
        Assert.Equal(parent, JsonDocument.Parse(result.Output).RootElement.GetProperty("parentId").GetString());
        Assert.True((await Get(parent)).GetProperty("isDerived").GetBoolean());
    }

    [Fact]
    public async Task Create_under_an_unknown_parent_passes_the_api_error_through()
    {
        var projectId = await CreateProject();

        var result = await CliRunner.Run(_http, "work-packages", "create", "--project", projectId,
            "--title", "X", "--parent", Guid.NewGuid().ToString());

        Assert.Equal(1, result.ExitCode);
        using var json = JsonDocument.Parse(result.Error);
        Assert.True(json.RootElement.GetProperty("error").GetProperty("errors").TryGetProperty("parentId", out _));
    }

    [Fact]
    public async Task Set_parent_moves_a_package_under_another_and_back_to_the_root()
    {
        var projectId = await CreateProject();
        var first = await CreatePackage(projectId, "Erstes");
        var second = await CreatePackage(projectId, "Zweites");

        var under = await CliRunner.Run(_http, "work-packages", "set-parent", second, "--parent", first, "--json");
        Assert.Equal(0, under.ExitCode);
        Assert.Equal(first, (await Get(second)).GetProperty("parentId").GetString());

        var toRoot = await CliRunner.Run(_http, "work-packages", "set-parent", second, "--root", "--json");
        Assert.Equal(0, toRoot.ExitCode);
        Assert.Equal(JsonValueKind.Null, (await Get(second)).GetProperty("parentId").ValueKind);
    }

    [Fact]
    public async Task Set_parent_requires_exactly_one_of_parent_and_root()
    {
        var projectId = await CreateProject();
        var package = await CreatePackage(projectId, "Paket");
        var other = await CreatePackage(projectId, "Anderes");

        var neither = await CliRunner.Run(_http, "work-packages", "set-parent", package);
        var both = await CliRunner.Run(_http, "work-packages", "set-parent", package, "--parent", other, "--root");

        Assert.NotEqual(0, neither.ExitCode);
        Assert.NotEqual(0, both.ExitCode);
    }

    [Fact]
    public async Task Set_parent_rejects_cycles_with_the_api_error()
    {
        var projectId = await CreateProject();
        var parent = await CreatePackage(projectId, "Parent");
        var child = await CreatePackage(projectId, "Child", parent);

        var result = await CliRunner.Run(_http, "work-packages", "set-parent", parent, "--parent", child);

        Assert.Equal(1, result.ExitCode);
        using var json = JsonDocument.Parse(result.Error);
        Assert.True(json.RootElement.GetProperty("error").GetProperty("errors").TryGetProperty("parentId", out _));
    }

    [Fact]
    public async Task Status_of_a_parent_is_derived_and_cannot_be_set()
    {
        var projectId = await CreateProject();
        var parent = await CreatePackage(projectId, "Parent");
        var child = await CreatePackage(projectId, "Child", parent);
        await CliRunner.Run(_http, "work-packages", "set-status", child, "done");

        var set = await CliRunner.Run(_http, "work-packages", "set-status", parent, "inProgress");

        Assert.Equal("done", (await Get(parent)).GetProperty("status").GetString());
        Assert.Equal(1, set.ExitCode);
        using var json = JsonDocument.Parse(set.Error);
        Assert.True(json.RootElement.GetProperty("error").GetProperty("errors").TryGetProperty("status", out _));
    }

    [Fact]
    public async Task Update_of_a_parent_does_not_send_its_derived_dates_back()
    {
        var projectId = await CreateProject();
        var parent = await CreatePackage(projectId, "Parent");
        await CreatePackage(projectId, "Child", parent, "--start-date", "2026-11-02", "--end-date", "2026-11-03");

        var result = await CliRunner.Run(_http, "work-packages", "update", parent, "--title", "Umbenannt");

        Assert.Equal(0, result.ExitCode);
        var updated = await Get(parent);
        Assert.Equal("Umbenannt", updated.GetProperty("title").GetString());
        Assert.Equal("2026-11-02", updated.GetProperty("startDate").GetString());
    }

    [Fact]
    public async Task Update_of_a_parent_with_explicit_dates_passes_the_api_error_through()
    {
        var projectId = await CreateProject();
        var parent = await CreatePackage(projectId, "Parent");
        await CreatePackage(projectId, "Child", parent);

        var result = await CliRunner.Run(_http, "work-packages", "update", parent, "--start-date", "2026-01-01");

        Assert.Equal(1, result.ExitCode);
        using var json = JsonDocument.Parse(result.Error);
        Assert.True(json.RootElement.GetProperty("error").GetProperty("errors").TryGetProperty("startDate", out _));
    }

    [Fact]
    public async Task List_table_shows_the_hierarchy_by_indenting_sub_packages()
    {
        var projectId = await CreateProject();
        var parent = await CreatePackage(projectId, "Netzwerk");
        var child = await CreatePackage(projectId, "Switch", parent);
        await CreatePackage(projectId, "Kabel", child);
        await CreatePackage(projectId, "Backup");

        var result = await CliRunner.Run(_http, "work-packages", "list", "--project", projectId);

        Assert.Equal(0, result.ExitCode);
        var lines = result.Output.Split('\n', StringSplitOptions.RemoveEmptyEntries);
        // The ID column is 36 characters wide plus two spaces of padding; the title column starts at 38.
        var titles = lines.Skip(1).Select(line => line[38..]).ToArray();
        Assert.StartsWith("Netzwerk", titles[0]);
        Assert.StartsWith("  Switch", titles[1]);
        Assert.StartsWith("    Kabel", titles[2]);
        Assert.StartsWith("Backup", titles[3]);
        Assert.Equal(4, titles.Length);
    }

    [Fact]
    public async Task List_json_stays_flat_and_carries_parent_ids()
    {
        var projectId = await CreateProject();
        var parent = await CreatePackage(projectId, "Parent");
        var child = await CreatePackage(projectId, "Child", parent);

        var result = await CliRunner.Run(_http, "work-packages", "list", "--project", projectId, "--json");

        var items = JsonDocument.Parse(result.Output).RootElement.EnumerateArray().ToArray();
        Assert.Equal(2, items.Length);
        Assert.Equal(parent, items.Single(i => i.GetProperty("id").GetString() == child).GetProperty("parentId").GetString());
    }

    [Fact]
    public async Task Deleting_a_parent_asks_with_a_hint_about_its_sub_packages()
    {
        var projectId = await CreateProject();
        var parent = await CreatePackage(projectId, "Parent");
        var child = await CreatePackage(projectId, "Child", parent);

        var result = await CliRunner.RunWithInput(_http, ["n"], "work-packages", "delete", parent);

        Assert.Equal(1, result.ExitCode);
        Assert.Contains("sub work packages", result.Error);
        Assert.Equal(0, (await CliRunner.Run(_http, "work-packages", "get", child)).ExitCode);

        var confirmed = await CliRunner.Run(_http, "work-packages", "delete", parent, "--yes");
        Assert.Equal(0, confirmed.ExitCode);
        Assert.Equal(1, (await CliRunner.Run(_http, "work-packages", "get", child)).ExitCode);
    }
}
