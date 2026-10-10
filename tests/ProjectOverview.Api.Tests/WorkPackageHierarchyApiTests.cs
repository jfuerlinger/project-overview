using System.Net;
using System.Net.Http.Json;
using System.Text.Json;

namespace ProjectOverview.Api.Tests;

/// <summary>
/// Seam 1: sub work packages (Unterarbeitspakete). A parent's status and dates are derived from its
/// children and cannot be set manually.
/// </summary>
public class WorkPackageHierarchyApiTests : IDisposable
{
    private readonly ApiFixture _api = new();
    private readonly HttpClient _client;

    public WorkPackageHierarchyApiTests() => _client = _api.CreateClient();

    public void Dispose()
    {
        _client.Dispose();
        _api.Dispose();
    }

    private static async Task<JsonElement> ReadJson(HttpResponseMessage response) =>
        await response.Content.ReadFromJsonAsync<JsonElement>();

    private async Task<Guid> CreateProject(string name = "Homelab") =>
        (await ReadJson(await _client.PostAsJsonAsync("/projects", new { name }))).GetProperty("id").GetGuid();

    private async Task<Guid> CreatePackage(
        Guid projectId, string title, Guid? parentId = null, string? startDate = null, string? endDate = null)
    {
        var response = await _client.PostAsJsonAsync(
            $"/projects/{projectId}/work-packages", new { title, parentId, startDate, endDate });
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        return (await ReadJson(response)).GetProperty("id").GetGuid();
    }

    private async Task SetStatus(Guid id, string status)
    {
        var response = await _client.PutAsJsonAsync($"/work-packages/{id}/status", new { status });
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    private async Task<JsonElement> Get(Guid id)
    {
        var response = await _client.GetAsync($"/work-packages/{id}");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return await ReadJson(response);
    }

    private static string[] Codes(JsonElement problem, string field) =>
        problem.GetProperty("errorCodes").GetProperty(field).EnumerateArray().Select(c => c.GetString()!).ToArray();

    // --- creating sub work packages ---

    [Fact]
    public async Task Sub_package_is_created_under_a_parent_in_the_same_project()
    {
        var projectId = await CreateProject();
        var parent = await CreatePackage(projectId, "Netzwerk");

        var child = await CreatePackage(projectId, "Switch kaufen", parent);

        var created = await Get(child);
        Assert.Equal(parent, created.GetProperty("parentId").GetGuid());
        Assert.Equal(JsonValueKind.Null, (await Get(parent)).GetProperty("parentId").ValueKind);
        var list = await ReadJson(await _client.GetAsync($"/projects/{projectId}/work-packages"));
        Assert.Equal(2, list.GetArrayLength());
    }

    [Fact]
    public async Task Sub_package_under_an_unknown_parent_is_rejected()
    {
        var projectId = await CreateProject();

        var response = await _client.PostAsJsonAsync($"/projects/{projectId}/work-packages",
            new { title = "X", parentId = Guid.NewGuid() });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal(["not_found"], Codes(await ReadJson(response), "parentId"));
    }

    [Fact]
    public async Task Sub_package_under_a_parent_of_another_project_is_rejected()
    {
        var first = await CreateProject("Erstes");
        var second = await CreateProject("Zweites");
        var foreignParent = await CreatePackage(second, "Fremd");

        var response = await _client.PostAsJsonAsync($"/projects/{first}/work-packages",
            new { title = "X", parentId = foreignParent });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal(["other_project"], Codes(await ReadJson(response), "parentId"));
        var list = await ReadJson(await _client.GetAsync($"/projects/{first}/work-packages"));
        Assert.Equal(0, list.GetArrayLength());
    }

    // --- derived status ---

    [Fact]
    public async Task Package_is_only_derived_while_it_has_children()
    {
        var projectId = await CreateProject();
        var parent = await CreatePackage(projectId, "Netzwerk");
        Assert.False((await Get(parent)).GetProperty("isDerived").GetBoolean());

        var child = await CreatePackage(projectId, "Switch", parent);
        Assert.True((await Get(parent)).GetProperty("isDerived").GetBoolean());
        Assert.False((await Get(child)).GetProperty("isDerived").GetBoolean());

        await _client.DeleteAsync($"/work-packages/{child}");
        Assert.False((await Get(parent)).GetProperty("isDerived").GetBoolean());
    }

    [Theory]
    [InlineData("open", "open")]
    [InlineData("inProgress", "inProgress")]
    [InlineData("done", "done")]
    [InlineData("discarded", "discarded")]
    [InlineData("open,open", "open")]
    [InlineData("done,done", "done")]
    [InlineData("done,open", "inProgress")]
    [InlineData("inProgress,open", "inProgress")]
    [InlineData("done,inProgress", "inProgress")]
    [InlineData("done,discarded", "done")]
    [InlineData("open,discarded", "open")]
    [InlineData("discarded,discarded", "discarded")]
    [InlineData("inProgress,discarded", "inProgress")]
    public async Task Parent_status_is_derived_from_its_children(string childStatuses, string expected)
    {
        var projectId = await CreateProject();
        var parent = await CreatePackage(projectId, "Parent");
        var index = 0;
        foreach (var status in childStatuses.Split(','))
        {
            var child = await CreatePackage(projectId, $"Child {++index}", parent);
            await SetStatus(child, status);
        }

        Assert.Equal(expected, (await Get(parent)).GetProperty("status").GetString());
        var list = await ReadJson(await _client.GetAsync($"/projects/{projectId}/work-packages"));
        var listed = list.EnumerateArray().Single(p => p.GetProperty("id").GetGuid() == parent);
        Assert.Equal(expected, listed.GetProperty("status").GetString());
    }

    [Fact]
    public async Task Derivation_is_applied_through_several_levels()
    {
        var projectId = await CreateProject();
        var root = await CreatePackage(projectId, "Root");
        var middle = await CreatePackage(projectId, "Middle", root);
        var leaf = await CreatePackage(projectId, "Leaf", middle);

        await SetStatus(leaf, "done");

        Assert.Equal("done", (await Get(middle)).GetProperty("status").GetString());
        Assert.Equal("done", (await Get(root)).GetProperty("status").GetString());
    }

    [Fact]
    public async Task Parent_keeps_its_own_status_again_when_it_loses_its_children()
    {
        var projectId = await CreateProject();
        var parent = await CreatePackage(projectId, "Parent");
        await SetStatus(parent, "inProgress");
        var child = await CreatePackage(projectId, "Child", parent);
        await SetStatus(child, "done");
        Assert.Equal("done", (await Get(parent)).GetProperty("status").GetString());

        await _client.DeleteAsync($"/work-packages/{child}");

        Assert.Equal("inProgress", (await Get(parent)).GetProperty("status").GetString());
    }

    // --- derived dates ---

    [Fact]
    public async Task Parent_dates_span_the_dates_of_its_children()
    {
        var projectId = await CreateProject();
        var parent = await CreatePackage(projectId, "Parent");
        await CreatePackage(projectId, "A", parent, startDate: "2026-11-10", endDate: "2026-11-12");
        await CreatePackage(projectId, "B", parent, startDate: "2026-11-02", endDate: "2026-11-05");
        await CreatePackage(projectId, "C", parent);

        var derived = await Get(parent);

        Assert.Equal("2026-11-02", derived.GetProperty("startDate").GetString());
        Assert.Equal("2026-11-12", derived.GetProperty("endDate").GetString());
    }

    [Fact]
    public async Task Parent_dates_are_empty_when_no_child_has_dates()
    {
        var projectId = await CreateProject();
        var parent = await CreatePackage(projectId, "Parent", startDate: "2026-01-01", endDate: "2026-01-31");
        await CreatePackage(projectId, "Child", parent);

        var derived = await Get(parent);

        Assert.Equal(JsonValueKind.Null, derived.GetProperty("startDate").ValueKind);
        Assert.Equal(JsonValueKind.Null, derived.GetProperty("endDate").ValueKind);
    }

    [Fact]
    public async Task Derived_dates_follow_nested_children()
    {
        var projectId = await CreateProject();
        var root = await CreatePackage(projectId, "Root");
        var middle = await CreatePackage(projectId, "Middle", root);
        await CreatePackage(projectId, "Leaf", middle, startDate: "2026-12-01", endDate: "2026-12-24");

        var derived = await Get(root);

        Assert.Equal("2026-12-01", derived.GetProperty("startDate").GetString());
        Assert.Equal("2026-12-24", derived.GetProperty("endDate").GetString());
    }

    // --- manual changes on a parent are rejected ---

    [Fact]
    public async Task Status_of_a_parent_cannot_be_set_manually()
    {
        var projectId = await CreateProject();
        var parent = await CreatePackage(projectId, "Parent");
        await CreatePackage(projectId, "Child", parent);

        var response = await _client.PutAsJsonAsync($"/work-packages/{parent}/status", new { status = "done" });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal(["derived"], Codes(await ReadJson(response), "status"));
        Assert.Equal("open", (await Get(parent)).GetProperty("status").GetString());
    }

    [Fact]
    public async Task Dates_of_a_parent_cannot_be_set_manually_but_other_fields_can_be_changed()
    {
        var projectId = await CreateProject();
        var parent = await CreatePackage(projectId, "Parent");
        await CreatePackage(projectId, "Child", parent, startDate: "2026-11-02", endDate: "2026-11-03");

        var withDates = await _client.PutAsJsonAsync($"/work-packages/{parent}", new
        {
            title = "Parent",
            startDate = "2026-01-01",
            endDate = "2026-01-31",
        });
        var withoutDates = await _client.PutAsJsonAsync($"/work-packages/{parent}", new
        {
            title = "Umbenannt",
            description = "Neu",
            durationDays = 5,
        });

        Assert.Equal(HttpStatusCode.BadRequest, withDates.StatusCode);
        var codes = await ReadJson(withDates);
        Assert.Equal(["derived"], Codes(codes, "startDate"));
        Assert.Equal(["derived"], Codes(codes, "endDate"));
        Assert.Equal(HttpStatusCode.OK, withoutDates.StatusCode);
        var updated = await ReadJson(withoutDates);
        Assert.Equal("Umbenannt", updated.GetProperty("title").GetString());
        Assert.Equal("2026-11-02", updated.GetProperty("startDate").GetString());
    }

    // --- moving ---

    [Fact]
    public async Task Package_can_be_moved_under_another_parent_and_back_to_the_root()
    {
        var projectId = await CreateProject();
        var first = await CreatePackage(projectId, "Erstes");
        var second = await CreatePackage(projectId, "Zweites");
        var child = await CreatePackage(projectId, "Kind", first);

        var toSecond = await _client.PutAsJsonAsync($"/work-packages/{child}/parent", new { parentId = second });
        Assert.Equal(HttpStatusCode.OK, toSecond.StatusCode);
        Assert.Equal(second, (await ReadJson(toSecond)).GetProperty("parentId").GetGuid());
        Assert.False((await Get(first)).GetProperty("isDerived").GetBoolean());
        Assert.True((await Get(second)).GetProperty("isDerived").GetBoolean());

        var toRoot = await _client.PutAsJsonAsync($"/work-packages/{child}/parent", new { parentId = (Guid?)null });
        Assert.Equal(HttpStatusCode.OK, toRoot.StatusCode);
        Assert.Equal(JsonValueKind.Null, (await ReadJson(toRoot)).GetProperty("parentId").ValueKind);
        Assert.False((await Get(second)).GetProperty("isDerived").GetBoolean());
    }

    [Fact]
    public async Task Package_cannot_become_its_own_descendant()
    {
        var projectId = await CreateProject();
        var root = await CreatePackage(projectId, "Root");
        var middle = await CreatePackage(projectId, "Middle", root);
        var leaf = await CreatePackage(projectId, "Leaf", middle);

        var underLeaf = await _client.PutAsJsonAsync($"/work-packages/{root}/parent", new { parentId = leaf });
        var underItself = await _client.PutAsJsonAsync($"/work-packages/{root}/parent", new { parentId = root });

        Assert.Equal(HttpStatusCode.BadRequest, underLeaf.StatusCode);
        Assert.Equal(["cycle"], Codes(await ReadJson(underLeaf), "parentId"));
        Assert.Equal(HttpStatusCode.BadRequest, underItself.StatusCode);
        Assert.Equal(["cycle"], Codes(await ReadJson(underItself), "parentId"));
        Assert.Equal(JsonValueKind.Null, (await Get(root)).GetProperty("parentId").ValueKind);
    }

    [Fact]
    public async Task Package_cannot_be_moved_into_another_project()
    {
        var first = await CreateProject("Erstes");
        var second = await CreateProject("Zweites");
        var package = await CreatePackage(first, "Paket");
        var foreign = await CreatePackage(second, "Fremd");

        var response = await _client.PutAsJsonAsync($"/work-packages/{package}/parent", new { parentId = foreign });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal(["other_project"], Codes(await ReadJson(response), "parentId"));
        Assert.Equal(first, (await Get(package)).GetProperty("projectId").GetGuid());
    }

    [Fact]
    public async Task Moving_under_an_unknown_parent_is_rejected_and_an_unknown_package_returns_404()
    {
        var projectId = await CreateProject();
        var package = await CreatePackage(projectId, "Paket");

        var unknownParent = await _client.PutAsJsonAsync($"/work-packages/{package}/parent", new { parentId = Guid.NewGuid() });
        var unknownPackage = await _client.PutAsJsonAsync($"/work-packages/{Guid.NewGuid()}/parent", new { parentId = package });

        Assert.Equal(HttpStatusCode.BadRequest, unknownParent.StatusCode);
        Assert.Equal(["not_found"], Codes(await ReadJson(unknownParent), "parentId"));
        Assert.Equal(HttpStatusCode.NotFound, unknownPackage.StatusCode);
    }

    [Fact]
    public async Task Editing_a_package_does_not_change_its_parent()
    {
        var projectId = await CreateProject();
        var parent = await CreatePackage(projectId, "Parent");
        var child = await CreatePackage(projectId, "Child", parent);

        var response = await _client.PutAsJsonAsync($"/work-packages/{child}", new { title = "Umbenannt" });

        Assert.Equal(parent, (await ReadJson(response)).GetProperty("parentId").GetGuid());
    }

    // --- deleting ---

    [Fact]
    public async Task Deleting_a_parent_deletes_its_sub_packages()
    {
        var projectId = await CreateProject();
        var parent = await CreatePackage(projectId, "Parent");
        var child = await CreatePackage(projectId, "Child", parent);
        var grandchild = await CreatePackage(projectId, "Grandchild", child);
        var other = await CreatePackage(projectId, "Other");

        var response = await _client.DeleteAsync($"/work-packages/{parent}");

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await _client.GetAsync($"/work-packages/{child}")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await _client.GetAsync($"/work-packages/{grandchild}")).StatusCode);
        var list = await ReadJson(await _client.GetAsync($"/projects/{projectId}/work-packages"));
        Assert.Equal([other], list.EnumerateArray().Select(p => p.GetProperty("id").GetGuid()));
    }

    [Fact]
    public async Task Deleting_a_project_removes_the_whole_hierarchy()
    {
        var projectId = await CreateProject();
        var parent = await CreatePackage(projectId, "Parent");
        var child = await CreatePackage(projectId, "Child", parent);

        var response = await _client.DeleteAsync($"/projects/{projectId}");

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await _client.GetAsync($"/work-packages/{child}")).StatusCode);
    }
}
