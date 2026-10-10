using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;

namespace ProjectOverview.Api.Tests;

/// <summary>Seam 1: reading, editing, changing the status of and deleting a project.</summary>
public class ProjectLifecycleApiTests : IDisposable
{
    private readonly ApiFixture _api = new();
    private readonly HttpClient _client;

    public ProjectLifecycleApiTests() => _client = _api.CreateClient();

    public void Dispose()
    {
        _client.Dispose();
        _api.Dispose();
    }

    private static async Task<JsonElement> ReadJson(HttpResponseMessage response) =>
        await response.Content.ReadFromJsonAsync<JsonElement>();

    private async Task<Guid> CreateProject(string name = "Homelab", string? description = "Alt") =>
        (await ReadJson(await _client.PostAsJsonAsync("/projects", new { name, description }))).GetProperty("id").GetGuid();

    private static StringContent RawJson(string json) => new(json, Encoding.UTF8, "application/json");

    // --- read ---

    [Fact]
    public async Task Project_can_be_read_by_id()
    {
        var id = await CreateProject("Homelab");

        var response = await _client.GetAsync($"/projects/{id}");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("Homelab", (await ReadJson(response)).GetProperty("name").GetString());
    }

    [Fact]
    public async Task Reading_an_unknown_project_returns_404()
    {
        var response = await _client.GetAsync($"/projects/{Guid.NewGuid()}");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    // --- edit ---

    [Fact]
    public async Task Name_description_and_dates_can_be_changed()
    {
        var id = await CreateProject("Homelab", "Alt");

        var response = await _client.PutAsJsonAsync($"/projects/{id}", new
        {
            name = "Homelab 2.0",
            description = "Neu",
            startDate = "2026-11-01",
            targetDate = "2027-01-31",
        });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var updated = await ReadJson(response);
        Assert.Equal("Homelab 2.0", updated.GetProperty("name").GetString());
        Assert.Equal("Neu", updated.GetProperty("description").GetString());
        Assert.Equal("2026-11-01", updated.GetProperty("startDate").GetString());
        Assert.Equal("2027-01-31", updated.GetProperty("targetDate").GetString());

        var reread = await ReadJson(await _client.GetAsync($"/projects/{id}"));
        Assert.Equal("Homelab 2.0", reread.GetProperty("name").GetString());
    }

    [Fact]
    public async Task Editing_replaces_the_fields_so_omitted_values_are_cleared()
    {
        var id = await CreateProject("Homelab", "Alt");
        await _client.PutAsJsonAsync($"/projects/{id}", new { name = "Homelab", startDate = "2026-11-01" });

        var response = await _client.PutAsJsonAsync($"/projects/{id}", new { name = "Homelab" });

        var updated = await ReadJson(response);
        Assert.Equal(JsonValueKind.Null, updated.GetProperty("description").ValueKind);
        Assert.Equal(JsonValueKind.Null, updated.GetProperty("startDate").ValueKind);
    }

    [Fact]
    public async Task Editing_does_not_change_the_status()
    {
        var id = await CreateProject();
        await _client.PutAsJsonAsync($"/projects/{id}/status", new { status = "inProgress" });

        var response = await _client.PutAsJsonAsync($"/projects/{id}", new { name = "Umbenannt" });

        Assert.Equal("inProgress", (await ReadJson(response)).GetProperty("status").GetString());
    }

    [Fact]
    public async Task Invalid_edit_is_rejected_and_leaves_the_project_unchanged()
    {
        var id = await CreateProject("Homelab");

        var blank = await _client.PutAsJsonAsync($"/projects/{id}", new { name = "  " });
        var backwards = await _client.PutAsJsonAsync($"/projects/{id}", new
        {
            name = "Rückwärts",
            startDate = "2026-12-31",
            targetDate = "2026-11-01",
        });

        Assert.Equal(HttpStatusCode.BadRequest, blank.StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, backwards.StatusCode);
        Assert.Equal("Homelab", (await ReadJson(await _client.GetAsync($"/projects/{id}"))).GetProperty("name").GetString());
    }

    [Fact]
    public async Task Editing_an_unknown_project_returns_404()
    {
        var response = await _client.PutAsJsonAsync($"/projects/{Guid.NewGuid()}", new { name = "X" });

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
        var id = await CreateProject();

        var response = await _client.PutAsJsonAsync($"/projects/{id}/status", new { status });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(status, (await ReadJson(response)).GetProperty("status").GetString());
        var list = await ReadJson(await _client.GetAsync("/projects"));
        Assert.Equal(status, list[0].GetProperty("status").GetString());
    }

    [Theory]
    [InlineData("""{ "status": "bogus" }""")]
    [InlineData("""{ "status": 7 }""")]
    [InlineData("""{ "status": 0 }""")]
    [InlineData("""{ }""")]
    public async Task Undefined_status_values_are_rejected(string body)
    {
        var id = await CreateProject();

        var response = await _client.PutAsync($"/projects/{id}/status", RawJson(body));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("open", (await ReadJson(await _client.GetAsync($"/projects/{id}"))).GetProperty("status").GetString());
    }

    [Fact]
    public async Task Setting_the_status_of_an_unknown_project_returns_404()
    {
        var response = await _client.PutAsJsonAsync($"/projects/{Guid.NewGuid()}/status", new { status = "done" });

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    // --- delete ---

    [Fact]
    public async Task Deleted_project_is_gone()
    {
        var id = await CreateProject("Weg");
        var other = await CreateProject("Bleibt");

        var response = await _client.DeleteAsync($"/projects/{id}");

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await _client.GetAsync($"/projects/{id}")).StatusCode);
        var list = await ReadJson(await _client.GetAsync("/projects"));
        Assert.Equal(1, list.GetArrayLength());
        Assert.Equal(other, list[0].GetProperty("id").GetGuid());
    }

    [Fact]
    public async Task Deleting_an_unknown_project_returns_404()
    {
        var response = await _client.DeleteAsync($"/projects/{Guid.NewGuid()}");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }
}
