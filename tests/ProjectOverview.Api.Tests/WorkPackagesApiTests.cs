using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;

namespace ProjectOverview.Api.Tests;

/// <summary>Seam 1: work packages (Arbeitspakete) belong to exactly one project.</summary>
public class WorkPackagesApiTests : IDisposable
{
    private readonly ApiFixture _api = new();
    private readonly HttpClient _client;

    public WorkPackagesApiTests() => _client = _api.CreateClient();

    public void Dispose()
    {
        _client.Dispose();
        _api.Dispose();
    }

    private static async Task<JsonElement> ReadJson(HttpResponseMessage response) =>
        await response.Content.ReadFromJsonAsync<JsonElement>();

    private async Task<Guid> CreateProject(string name = "Homelab") =>
        (await ReadJson(await _client.PostAsJsonAsync("/projects", new { name }))).GetProperty("id").GetGuid();

    private async Task<Guid> CreateWorkPackage(Guid projectId, string title = "Switch kaufen")
    {
        var response = await _client.PostAsJsonAsync($"/projects/{projectId}/work-packages", new { title });
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        return (await ReadJson(response)).GetProperty("id").GetGuid();
    }

    private static StringContent RawJson(string json) => new(json, Encoding.UTF8, "application/json");

    // --- create and list ---

    [Fact]
    public async Task Work_package_is_created_in_a_project_and_listed()
    {
        var projectId = await CreateProject();

        var create = await _client.PostAsJsonAsync($"/projects/{projectId}/work-packages", new
        {
            title = "Switch kaufen",
            description = "24 Ports, PoE",
            durationDays = 3,
            startDate = "2026-11-02",
            endDate = "2026-11-04",
        });

        Assert.Equal(HttpStatusCode.Created, create.StatusCode);
        var created = await ReadJson(create);
        Assert.Equal(projectId, created.GetProperty("projectId").GetGuid());
        Assert.Equal("Switch kaufen", created.GetProperty("title").GetString());
        Assert.Equal("24 Ports, PoE", created.GetProperty("description").GetString());
        Assert.Equal(3, created.GetProperty("durationDays").GetInt32());
        Assert.Equal("2026-11-02", created.GetProperty("startDate").GetString());
        Assert.Equal("2026-11-04", created.GetProperty("endDate").GetString());
        Assert.Equal("open", created.GetProperty("status").GetString());

        var list = await ReadJson(await _client.GetAsync($"/projects/{projectId}/work-packages"));
        Assert.Equal(1, list.GetArrayLength());
        Assert.Equal(created.GetProperty("id").GetGuid(), list[0].GetProperty("id").GetGuid());
    }

    [Fact]
    public async Task Description_duration_and_dates_are_optional_and_nothing_is_calculated()
    {
        var projectId = await CreateProject();

        var create = await _client.PostAsJsonAsync($"/projects/{projectId}/work-packages", new
        {
            title = "Planen",
            durationDays = 5,
            startDate = "2026-11-02",
        });

        var created = await ReadJson(create);
        Assert.Equal(JsonValueKind.Null, created.GetProperty("description").ValueKind);
        Assert.Equal(JsonValueKind.Null, created.GetProperty("endDate").ValueKind);

        var minimal = await ReadJson(await _client.PostAsJsonAsync($"/projects/{projectId}/work-packages", new { title = "Minimal" }));
        Assert.Equal(JsonValueKind.Null, minimal.GetProperty("durationDays").ValueKind);
        Assert.Equal(JsonValueKind.Null, minimal.GetProperty("startDate").ValueKind);
        Assert.Equal(JsonValueKind.Null, minimal.GetProperty("endDate").ValueKind);
    }

    [Fact]
    public async Task List_contains_only_the_packages_of_that_project_in_creation_order()
    {
        var first = await CreateProject("Erstes");
        var second = await CreateProject("Zweites");
        await CreateWorkPackage(first, "A1");
        await CreateWorkPackage(second, "B1");
        await CreateWorkPackage(first, "A2");

        var list = await ReadJson(await _client.GetAsync($"/projects/{first}/work-packages"));

        Assert.Equal(["A1", "A2"], list.EnumerateArray().Select(p => p.GetProperty("title").GetString()));
    }

    [Fact]
    public async Task Packages_in_an_unknown_project_cannot_be_listed_or_created()
    {
        var unknown = Guid.NewGuid();

        var list = await _client.GetAsync($"/projects/{unknown}/work-packages");
        var create = await _client.PostAsJsonAsync($"/projects/{unknown}/work-packages", new { title = "X" });

        Assert.Equal(HttpStatusCode.NotFound, list.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, create.StatusCode);
    }

    // --- validation ---

    [Fact]
    public async Task Title_is_required()
    {
        var projectId = await CreateProject();

        var response = await _client.PostAsJsonAsync($"/projects/{projectId}/work-packages", new { title = "  " });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var codes = (await ReadJson(response)).GetProperty("errorCodes");
        Assert.Equal(["required"], codes.GetProperty("title").EnumerateArray().Select(c => c.GetString()));
        var list = await ReadJson(await _client.GetAsync($"/projects/{projectId}/work-packages"));
        Assert.Equal(0, list.GetArrayLength());
    }

    [Fact]
    public async Task End_date_must_not_be_before_start_date()
    {
        var projectId = await CreateProject();

        var response = await _client.PostAsJsonAsync($"/projects/{projectId}/work-packages", new
        {
            title = "Rückwärts",
            startDate = "2026-11-10",
            endDate = "2026-11-01",
        });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var codes = (await ReadJson(response)).GetProperty("errorCodes");
        Assert.Equal(["before_start_date"], codes.GetProperty("endDate").EnumerateArray().Select(c => c.GetString()));
    }

    [Theory]
    [InlineData(0, "min_value:1")]
    [InlineData(-3, "min_value:1")]
    [InlineData(3651, "max_value:3650")]
    public async Task Duration_must_be_within_limits(int durationDays, string expectedCode)
    {
        var projectId = await CreateProject();

        var response = await _client.PostAsJsonAsync($"/projects/{projectId}/work-packages", new { title = "X", durationDays });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var codes = (await ReadJson(response)).GetProperty("errorCodes");
        Assert.Equal([expectedCode], codes.GetProperty("durationDays").EnumerateArray().Select(c => c.GetString()));
    }

    // --- read, update ---

    [Fact]
    public async Task Work_package_can_be_read_by_id()
    {
        var projectId = await CreateProject();
        var id = await CreateWorkPackage(projectId, "Switch kaufen");

        var response = await _client.GetAsync($"/work-packages/{id}");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("Switch kaufen", (await ReadJson(response)).GetProperty("title").GetString());
    }

    [Fact]
    public async Task Reading_an_unknown_work_package_returns_404()
    {
        var response = await _client.GetAsync($"/work-packages/{Guid.NewGuid()}");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task Update_replaces_the_fields_and_keeps_project_and_status()
    {
        var projectId = await CreateProject();
        var id = await CreateWorkPackage(projectId, "Alt");
        await _client.PutAsJsonAsync($"/work-packages/{id}/status", new { status = "inProgress" });

        var response = await _client.PutAsJsonAsync($"/work-packages/{id}", new
        {
            title = "Neu",
            description = "Beschreibung",
            durationDays = 2,
            startDate = "2026-11-02",
            endDate = "2026-11-03",
        });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var updated = await ReadJson(response);
        Assert.Equal("Neu", updated.GetProperty("title").GetString());
        Assert.Equal(2, updated.GetProperty("durationDays").GetInt32());
        Assert.Equal(projectId, updated.GetProperty("projectId").GetGuid());
        Assert.Equal("inProgress", updated.GetProperty("status").GetString());
    }

    [Fact]
    public async Task Update_clears_omitted_optional_values()
    {
        var projectId = await CreateProject();
        var response = await _client.PostAsJsonAsync($"/projects/{projectId}/work-packages", new
        {
            title = "Alt",
            description = "Weg damit",
            durationDays = 4,
            startDate = "2026-11-02",
            endDate = "2026-11-05",
        });
        var id = (await ReadJson(response)).GetProperty("id").GetGuid();

        var updated = await ReadJson(await _client.PutAsJsonAsync($"/work-packages/{id}", new { title = "Alt" }));

        Assert.Equal(JsonValueKind.Null, updated.GetProperty("description").ValueKind);
        Assert.Equal(JsonValueKind.Null, updated.GetProperty("durationDays").ValueKind);
        Assert.Equal(JsonValueKind.Null, updated.GetProperty("startDate").ValueKind);
        Assert.Equal(JsonValueKind.Null, updated.GetProperty("endDate").ValueKind);
    }

    [Fact]
    public async Task Invalid_update_is_rejected_and_leaves_the_package_unchanged()
    {
        var projectId = await CreateProject();
        var id = await CreateWorkPackage(projectId, "Bleibt");

        var response = await _client.PutAsJsonAsync($"/work-packages/{id}", new { title = "" });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("Bleibt", (await ReadJson(await _client.GetAsync($"/work-packages/{id}"))).GetProperty("title").GetString());
    }

    [Fact]
    public async Task Updating_an_unknown_work_package_returns_404()
    {
        var response = await _client.PutAsJsonAsync($"/work-packages/{Guid.NewGuid()}", new { title = "X" });

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    // --- status ---

    [Theory]
    [InlineData("open")]
    [InlineData("inProgress")]
    [InlineData("done")]
    [InlineData("discarded")]
    public async Task Each_defined_status_can_be_set(string status)
    {
        var projectId = await CreateProject();
        var id = await CreateWorkPackage(projectId);

        var response = await _client.PutAsJsonAsync($"/work-packages/{id}/status", new { status });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(status, (await ReadJson(response)).GetProperty("status").GetString());
        var list = await ReadJson(await _client.GetAsync($"/projects/{projectId}/work-packages"));
        Assert.Equal(status, list[0].GetProperty("status").GetString());
    }

    [Theory]
    [InlineData("""{ "status": "bogus" }""")]
    [InlineData("""{ "status": 2 }""")]
    [InlineData("""{ }""")]
    public async Task Undefined_status_values_are_rejected(string body)
    {
        var projectId = await CreateProject();
        var id = await CreateWorkPackage(projectId);

        var response = await _client.PutAsync($"/work-packages/{id}/status", RawJson(body));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("open", (await ReadJson(await _client.GetAsync($"/work-packages/{id}"))).GetProperty("status").GetString());
    }

    [Fact]
    public async Task Setting_the_status_of_an_unknown_work_package_returns_404()
    {
        var response = await _client.PutAsJsonAsync($"/work-packages/{Guid.NewGuid()}/status", new { status = "done" });

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    // --- delete ---

    [Fact]
    public async Task Deleted_work_package_is_gone_and_others_remain()
    {
        var projectId = await CreateProject();
        var gone = await CreateWorkPackage(projectId, "Weg");
        var stays = await CreateWorkPackage(projectId, "Bleibt");

        var response = await _client.DeleteAsync($"/work-packages/{gone}");

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await _client.GetAsync($"/work-packages/{gone}")).StatusCode);
        var list = await ReadJson(await _client.GetAsync($"/projects/{projectId}/work-packages"));
        Assert.Equal([stays], list.EnumerateArray().Select(p => p.GetProperty("id").GetGuid()));
    }

    [Fact]
    public async Task Deleting_an_unknown_work_package_returns_404()
    {
        var response = await _client.DeleteAsync($"/work-packages/{Guid.NewGuid()}");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task Deleting_a_project_removes_its_work_packages_but_not_those_of_other_projects()
    {
        var doomed = await CreateProject("Weg");
        var survivor = await CreateProject("Bleibt");
        var doomedPackage = await CreateWorkPackage(doomed);
        var survivingPackage = await CreateWorkPackage(survivor);

        await _client.DeleteAsync($"/projects/{doomed}");

        Assert.Equal(HttpStatusCode.NotFound, (await _client.GetAsync($"/work-packages/{doomedPackage}")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await _client.GetAsync($"/work-packages/{survivingPackage}")).StatusCode);
    }
}
