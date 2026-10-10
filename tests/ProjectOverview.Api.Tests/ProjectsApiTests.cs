using System.Net;
using System.Net.Http.Json;
using System.Text.Json;

namespace ProjectOverview.Api.Tests;

public class ProjectsApiTests : IDisposable
{
    private readonly ApiFixture _api = new();
    private readonly HttpClient _client;

    public ProjectsApiTests() => _client = _api.CreateClient();

    public void Dispose()
    {
        _client.Dispose();
        _api.Dispose();
    }

    private static async Task<JsonElement> ReadJson(HttpResponseMessage response) =>
        await response.Content.ReadFromJsonAsync<JsonElement>();

    [Fact]
    public async Task Project_list_is_empty_for_a_new_database()
    {
        var response = await _client.GetAsync("/projects");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var projects = await ReadJson(response);
        Assert.Equal(0, projects.GetArrayLength());
    }

    [Fact]
    public async Task Created_project_is_returned_and_appears_in_the_list()
    {
        var create = await _client.PostAsJsonAsync("/projects", new
        {
            name = "Homelab umbauen",
            description = "Netzwerk neu aufsetzen",
            startDate = "2026-11-01",
            targetDate = "2026-12-31",
        });

        Assert.Equal(HttpStatusCode.Created, create.StatusCode);
        var created = await ReadJson(create);
        Assert.Equal("Homelab umbauen", created.GetProperty("name").GetString());
        Assert.Equal("Netzwerk neu aufsetzen", created.GetProperty("description").GetString());
        Assert.Equal("2026-11-01", created.GetProperty("startDate").GetString());
        Assert.Equal("2026-12-31", created.GetProperty("targetDate").GetString());
        Assert.Equal("open", created.GetProperty("status").GetString());
        var id = created.GetProperty("id").GetGuid();
        Assert.NotEqual(Guid.Empty, id);

        var list = await ReadJson(await _client.GetAsync("/projects"));
        Assert.Equal(1, list.GetArrayLength());
        Assert.Equal(id, list[0].GetProperty("id").GetGuid());
        Assert.Equal("Homelab umbauen", list[0].GetProperty("name").GetString());
    }

    [Fact]
    public async Task Project_dates_and_description_are_optional()
    {
        var create = await _client.PostAsJsonAsync("/projects", new { name = "Auto" });

        Assert.Equal(HttpStatusCode.Created, create.StatusCode);
        var created = await ReadJson(create);
        Assert.Equal(JsonValueKind.Null, created.GetProperty("startDate").ValueKind);
        Assert.Equal(JsonValueKind.Null, created.GetProperty("targetDate").ValueKind);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public async Task Project_without_a_name_is_rejected(string name)
    {
        var response = await _client.PostAsJsonAsync("/projects", new { name });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var list = await ReadJson(await _client.GetAsync("/projects"));
        Assert.Equal(0, list.GetArrayLength());
    }

    [Fact]
    public async Task Project_with_target_date_before_start_date_is_rejected()
    {
        var response = await _client.PostAsJsonAsync("/projects", new
        {
            name = "Rückwärts",
            startDate = "2026-12-31",
            targetDate = "2026-11-01",
        });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Validation_errors_carry_stable_error_codes_next_to_the_messages()
    {
        var response = await _client.PostAsJsonAsync("/projects", new
        {
            name = "  ",
            startDate = "2026-12-31",
            targetDate = "2026-11-01",
        });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var problem = await ReadJson(response);
        var codes = problem.GetProperty("errorCodes");
        Assert.Equal(["required"], codes.GetProperty("name").EnumerateArray().Select(c => c.GetString()));
        Assert.Equal(["before_start_date"], codes.GetProperty("targetDate").EnumerateArray().Select(c => c.GetString()));
        // Each code lines up with the English message at the same position.
        Assert.Equal(1, problem.GetProperty("errors").GetProperty("name").GetArrayLength());
    }

    [Fact]
    public async Task Too_long_name_reports_its_limit_in_the_error_code()
    {
        var response = await _client.PostAsJsonAsync("/projects", new { name = new string('x', 201) });

        var codes = (await ReadJson(response)).GetProperty("errorCodes");
        Assert.Equal(["max_length:200"], codes.GetProperty("name").EnumerateArray().Select(c => c.GetString()));
    }

    [Fact]
    public async Task Whitespace_only_description_is_stored_as_no_description()
    {
        var create = await _client.PostAsJsonAsync("/projects", new { name = "Auto", description = "   " });

        Assert.Equal(HttpStatusCode.Created, create.StatusCode);
        var created = await ReadJson(create);
        Assert.Equal(JsonValueKind.Null, created.GetProperty("description").ValueKind);
    }

    [Fact]
    public async Task Projects_are_listed_in_creation_order()
    {
        await _client.PostAsJsonAsync("/projects", new { name = "Erstes" });
        await _client.PostAsJsonAsync("/projects", new { name = "Zweites" });

        var list = await ReadJson(await _client.GetAsync("/projects"));

        Assert.Equal(["Erstes", "Zweites"], list.EnumerateArray().Select(p => p.GetProperty("name").GetString()));
    }
}
