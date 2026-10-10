using System.Text.Json;
using System.Text.Json.Nodes;

namespace ProjectOverview.Api.Tests;

/// <summary>
/// The checked-in OpenAPI document is the source of truth for the generated CLI client and the
/// agent skill. This test fails when the running API drifts from it.
/// Regenerate with: UPDATE_OPENAPI=1 dotnet test --filter OpenApiContractTests
/// </summary>
public class OpenApiContractTests
{
    private static readonly JsonSerializerOptions Pretty = new() { WriteIndented = true };

    [Fact]
    public async Task Checked_in_openapi_document_matches_the_running_api()
    {
        using var api = new ApiFixture();
        using var client = api.CreateClient();
        var live = JsonNode.Parse(await client.GetStringAsync("/openapi/v1.json"))!.AsObject();
        // The server URL depends on the host the document is requested from.
        live.Remove("servers");

        var path = Path.Combine(FindRepositoryRoot(), "src", "ProjectOverview.Client", "openapi.json");

        if (Environment.GetEnvironmentVariable("UPDATE_OPENAPI") == "1")
        {
            File.WriteAllText(path, live.ToJsonString(Pretty) + Environment.NewLine);
            return;
        }

        Assert.True(File.Exists(path), $"{path} is missing. Run: UPDATE_OPENAPI=1 dotnet test --filter OpenApiContractTests");
        var checkedIn = JsonNode.Parse(File.ReadAllText(path))!;
        Assert.True(
            JsonNode.DeepEquals(live, checkedIn),
            "The API's OpenAPI document differs from src/ProjectOverview.Client/openapi.json. " +
            "Run: UPDATE_OPENAPI=1 dotnet test --filter OpenApiContractTests");
    }

    private static string FindRepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "ProjectOverview.slnx")))
            directory = directory.Parent;
        return directory?.FullName ?? throw new InvalidOperationException("Repository root not found.");
    }
}
