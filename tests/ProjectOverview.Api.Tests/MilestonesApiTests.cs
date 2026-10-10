using System.Net;
using System.Net.Http.Json;
using System.Text.Json;

namespace ProjectOverview.Api.Tests;

/// <summary>Seam 1: milestones (Meilensteine) and the assignment of work packages to them.</summary>
public class MilestonesApiTests : IDisposable
{
    private readonly ApiFixture _api = new();
    private readonly HttpClient _client;

    public MilestonesApiTests() => _client = _api.CreateClient();

    public void Dispose()
    {
        _client.Dispose();
        _api.Dispose();
    }

    private static async Task<JsonElement> ReadJson(HttpResponseMessage response) =>
        await response.Content.ReadFromJsonAsync<JsonElement>();

    private async Task<Guid> CreateProject(string name = "Homelab") =>
        (await ReadJson(await _client.PostAsJsonAsync("/projects", new { name }))).GetProperty("id").GetGuid();

    private async Task<Guid> CreateMilestone(Guid projectId, string title = "Netzwerk steht", string targetDate = "2026-12-01")
    {
        var response = await _client.PostAsJsonAsync($"/projects/{projectId}/milestones", new { title, targetDate });
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        return (await ReadJson(response)).GetProperty("id").GetGuid();
    }

    private async Task<Guid> CreateWorkPackage(Guid projectId, string title = "Switch kaufen") =>
        (await ReadJson(await _client.PostAsJsonAsync($"/projects/{projectId}/work-packages", new { title })))
            .GetProperty("id").GetGuid();

    private Task<HttpResponseMessage> Assign(Guid workPackageId, Guid? milestoneId) =>
        _client.PutAsJsonAsync($"/work-packages/{workPackageId}/milestone", new { milestoneId });

    // --- create, list, read ---

    [Fact]
    public async Task Milestone_is_created_in_a_project_and_listed()
    {
        var projectId = await CreateProject();

        var create = await _client.PostAsJsonAsync($"/projects/{projectId}/milestones", new
        {
            title = "Netzwerk steht",
            description = "Alle Switches in Betrieb",
            targetDate = "2026-12-01",
        });

        Assert.Equal(HttpStatusCode.Created, create.StatusCode);
        var created = await ReadJson(create);
        Assert.Equal(projectId, created.GetProperty("projectId").GetGuid());
        Assert.Equal("Netzwerk steht", created.GetProperty("title").GetString());
        Assert.Equal("Alle Switches in Betrieb", created.GetProperty("description").GetString());
        Assert.Equal("2026-12-01", created.GetProperty("targetDate").GetString());

        var list = await ReadJson(await _client.GetAsync($"/projects/{projectId}/milestones"));
        Assert.Equal(1, list.GetArrayLength());
        Assert.Equal(created.GetProperty("id").GetGuid(), list[0].GetProperty("id").GetGuid());
    }

    [Fact]
    public async Task Milestone_has_neither_duration_nor_status()
    {
        var projectId = await CreateProject();
        var id = await CreateMilestone(projectId);

        var milestone = await ReadJson(await _client.GetAsync($"/milestones/{id}"));

        Assert.False(milestone.TryGetProperty("durationDays", out _));
        Assert.False(milestone.TryGetProperty("status", out _));
    }

    [Fact]
    public async Task Status_of_a_milestone_cannot_be_set()
    {
        var projectId = await CreateProject();
        var id = await CreateMilestone(projectId);

        var response = await _client.PutAsJsonAsync($"/milestones/{id}/status", new { status = "done" });

        Assert.True(response.StatusCode is HttpStatusCode.NotFound or HttpStatusCode.MethodNotAllowed);
    }

    [Fact]
    public async Task List_contains_only_the_milestones_of_that_project_ordered_by_target_date()
    {
        var first = await CreateProject("Erstes");
        var second = await CreateProject("Zweites");
        await CreateMilestone(first, "Spät", "2027-03-01");
        await CreateMilestone(second, "Fremd", "2026-01-01");
        await CreateMilestone(first, "Früh", "2026-12-01");

        var list = await ReadJson(await _client.GetAsync($"/projects/{first}/milestones"));

        Assert.Equal(["Früh", "Spät"], list.EnumerateArray().Select(m => m.GetProperty("title").GetString()));
    }

    [Fact]
    public async Task Milestones_in_an_unknown_project_cannot_be_listed_or_created()
    {
        var unknown = Guid.NewGuid();

        var list = await _client.GetAsync($"/projects/{unknown}/milestones");
        var create = await _client.PostAsJsonAsync($"/projects/{unknown}/milestones", new { title = "X", targetDate = "2026-12-01" });

        Assert.Equal(HttpStatusCode.NotFound, list.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, create.StatusCode);
    }

    [Fact]
    public async Task Milestone_can_be_read_by_id_and_unknown_id_returns_404()
    {
        var projectId = await CreateProject();
        var id = await CreateMilestone(projectId, "Netzwerk steht");

        var found = await _client.GetAsync($"/milestones/{id}");
        var missing = await _client.GetAsync($"/milestones/{Guid.NewGuid()}");

        Assert.Equal(HttpStatusCode.OK, found.StatusCode);
        Assert.Equal("Netzwerk steht", (await ReadJson(found)).GetProperty("title").GetString());
        Assert.Equal(HttpStatusCode.NotFound, missing.StatusCode);
    }

    // --- validation ---

    [Fact]
    public async Task Title_and_target_date_are_required()
    {
        var projectId = await CreateProject();

        var response = await _client.PostAsJsonAsync($"/projects/{projectId}/milestones", new { title = "  " });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var codes = (await ReadJson(response)).GetProperty("errorCodes");
        Assert.Equal(["required"], codes.GetProperty("title").EnumerateArray().Select(c => c.GetString()));
        Assert.Equal(["required"], codes.GetProperty("targetDate").EnumerateArray().Select(c => c.GetString()));
        var list = await ReadJson(await _client.GetAsync($"/projects/{projectId}/milestones"));
        Assert.Equal(0, list.GetArrayLength());
    }

    [Fact]
    public async Task Too_long_title_reports_its_limit()
    {
        var projectId = await CreateProject();

        var response = await _client.PostAsJsonAsync($"/projects/{projectId}/milestones",
            new { title = new string('x', 201), targetDate = "2026-12-01" });

        var codes = (await ReadJson(response)).GetProperty("errorCodes");
        Assert.Equal(["max_length:200"], codes.GetProperty("title").EnumerateArray().Select(c => c.GetString()));
    }

    // --- update ---

    [Fact]
    public async Task Update_replaces_the_fields_and_clears_omitted_description()
    {
        var projectId = await CreateProject();
        var create = await _client.PostAsJsonAsync($"/projects/{projectId}/milestones",
            new { title = "Alt", description = "Weg damit", targetDate = "2026-12-01" });
        var id = (await ReadJson(create)).GetProperty("id").GetGuid();

        var response = await _client.PutAsJsonAsync($"/milestones/{id}", new { title = "Neu", targetDate = "2027-01-15" });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var updated = await ReadJson(response);
        Assert.Equal("Neu", updated.GetProperty("title").GetString());
        Assert.Equal("2027-01-15", updated.GetProperty("targetDate").GetString());
        Assert.Equal(JsonValueKind.Null, updated.GetProperty("description").ValueKind);
        Assert.Equal(projectId, updated.GetProperty("projectId").GetGuid());
    }

    [Fact]
    public async Task Invalid_update_is_rejected_and_leaves_the_milestone_unchanged()
    {
        var projectId = await CreateProject();
        var id = await CreateMilestone(projectId, "Bleibt");

        var response = await _client.PutAsJsonAsync($"/milestones/{id}", new { title = "", targetDate = "2026-12-01" });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("Bleibt", (await ReadJson(await _client.GetAsync($"/milestones/{id}"))).GetProperty("title").GetString());
    }

    [Fact]
    public async Task Updating_an_unknown_milestone_returns_404()
    {
        var response = await _client.PutAsJsonAsync($"/milestones/{Guid.NewGuid()}", new { title = "X", targetDate = "2026-12-01" });

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task Updating_a_milestone_keeps_its_assigned_work_packages()
    {
        var projectId = await CreateProject();
        var milestoneId = await CreateMilestone(projectId);
        var packageId = await CreateWorkPackage(projectId);
        await Assign(packageId, milestoneId);

        await _client.PutAsJsonAsync($"/milestones/{milestoneId}", new { title = "Umbenannt", targetDate = "2027-02-01" });

        var package = await ReadJson(await _client.GetAsync($"/work-packages/{packageId}"));
        Assert.Equal(milestoneId, package.GetProperty("milestoneId").GetGuid());
    }

    // --- delete ---

    [Fact]
    public async Task Deleted_milestone_is_gone_but_its_work_packages_remain_unassigned()
    {
        var projectId = await CreateProject();
        var milestoneId = await CreateMilestone(projectId);
        var packageId = await CreateWorkPackage(projectId);
        await Assign(packageId, milestoneId);

        var response = await _client.DeleteAsync($"/milestones/{milestoneId}");

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await _client.GetAsync($"/milestones/{milestoneId}")).StatusCode);
        var package = await _client.GetAsync($"/work-packages/{packageId}");
        Assert.Equal(HttpStatusCode.OK, package.StatusCode);
        Assert.Equal(JsonValueKind.Null, (await ReadJson(package)).GetProperty("milestoneId").ValueKind);
    }

    [Fact]
    public async Task Deleting_an_unknown_milestone_returns_404()
    {
        var response = await _client.DeleteAsync($"/milestones/{Guid.NewGuid()}");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task Deleting_a_project_removes_its_milestones_but_not_those_of_other_projects()
    {
        var doomed = await CreateProject("Weg");
        var survivor = await CreateProject("Bleibt");
        var doomedMilestone = await CreateMilestone(doomed);
        var survivingMilestone = await CreateMilestone(survivor);

        await _client.DeleteAsync($"/projects/{doomed}");

        Assert.Equal(HttpStatusCode.NotFound, (await _client.GetAsync($"/milestones/{doomedMilestone}")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await _client.GetAsync($"/milestones/{survivingMilestone}")).StatusCode);
    }

    // --- assignment ---

    [Fact]
    public async Task Work_package_starts_unassigned_and_can_be_assigned_to_a_milestone()
    {
        var projectId = await CreateProject();
        var milestoneId = await CreateMilestone(projectId);
        var packageId = await CreateWorkPackage(projectId);
        var before = await ReadJson(await _client.GetAsync($"/work-packages/{packageId}"));
        Assert.Equal(JsonValueKind.Null, before.GetProperty("milestoneId").ValueKind);

        var response = await Assign(packageId, milestoneId);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(milestoneId, (await ReadJson(response)).GetProperty("milestoneId").GetGuid());
        var list = await ReadJson(await _client.GetAsync($"/projects/{projectId}/work-packages"));
        Assert.Equal(milestoneId, list[0].GetProperty("milestoneId").GetGuid());
    }

    [Fact]
    public async Task Assignment_can_be_removed_and_moved_to_another_milestone()
    {
        var projectId = await CreateProject();
        var first = await CreateMilestone(projectId, "Erster");
        var second = await CreateMilestone(projectId, "Zweiter");
        var packageId = await CreateWorkPackage(projectId);
        await Assign(packageId, first);

        var moved = await Assign(packageId, second);
        var removed = await Assign(packageId, null);

        Assert.Equal(second, (await ReadJson(moved)).GetProperty("milestoneId").GetGuid());
        Assert.Equal(JsonValueKind.Null, (await ReadJson(removed)).GetProperty("milestoneId").ValueKind);
    }

    [Fact]
    public async Task Several_work_packages_can_share_a_milestone()
    {
        var projectId = await CreateProject();
        var milestoneId = await CreateMilestone(projectId);
        var a = await CreateWorkPackage(projectId, "A");
        var b = await CreateWorkPackage(projectId, "B");

        await Assign(a, milestoneId);
        await Assign(b, milestoneId);

        var list = await ReadJson(await _client.GetAsync($"/projects/{projectId}/work-packages"));
        Assert.All(list.EnumerateArray(), p => Assert.Equal(milestoneId, p.GetProperty("milestoneId").GetGuid()));
    }

    [Fact]
    public async Task Assigning_to_a_milestone_of_another_project_is_rejected()
    {
        var projectA = await CreateProject("A");
        var projectB = await CreateProject("B");
        var foreignMilestone = await CreateMilestone(projectB);
        var packageId = await CreateWorkPackage(projectA);

        var response = await Assign(packageId, foreignMilestone);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var codes = (await ReadJson(response)).GetProperty("errorCodes");
        Assert.Equal(["other_project"], codes.GetProperty("milestoneId").EnumerateArray().Select(c => c.GetString()));
        var package = await ReadJson(await _client.GetAsync($"/work-packages/{packageId}"));
        Assert.Equal(JsonValueKind.Null, package.GetProperty("milestoneId").ValueKind);
    }

    [Fact]
    public async Task Assigning_to_an_unknown_milestone_is_rejected()
    {
        var projectId = await CreateProject();
        var packageId = await CreateWorkPackage(projectId);

        var response = await Assign(packageId, Guid.NewGuid());

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var codes = (await ReadJson(response)).GetProperty("errorCodes");
        Assert.Equal(["not_found"], codes.GetProperty("milestoneId").EnumerateArray().Select(c => c.GetString()));
    }

    [Fact]
    public async Task Assigning_an_unknown_work_package_returns_404()
    {
        var response = await Assign(Guid.NewGuid(), null);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task Editing_a_work_package_keeps_its_milestone()
    {
        var projectId = await CreateProject();
        var milestoneId = await CreateMilestone(projectId);
        var packageId = await CreateWorkPackage(projectId);
        await Assign(packageId, milestoneId);

        var response = await _client.PutAsJsonAsync($"/work-packages/{packageId}", new { title = "Umbenannt" });

        Assert.Equal(milestoneId, (await ReadJson(response)).GetProperty("milestoneId").GetGuid());
    }
}
