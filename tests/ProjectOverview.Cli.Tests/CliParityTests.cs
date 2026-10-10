using System.Text.Json;
using ProjectOverview.Api.Tests;
using ProjectOverview.Cli;

namespace ProjectOverview.Cli.Tests;

/// <summary>Seam 2: every API operation must be reachable through the CLI (ADR-0003).</summary>
public class CliParityTests
{
    [Fact]
    public async Task Every_api_operation_has_a_cli_command()
    {
        using var api = new ApiFixture();
        using var client = api.CreateClient();
        using var document = JsonDocument.Parse(await client.GetStringAsync("/openapi/v1.json"));

        var apiOperations = document.RootElement.GetProperty("paths").EnumerateObject()
            .SelectMany(path => path.Value.EnumerateObject())
            .Select(operation => operation.Value.GetProperty("operationId").GetString()!)
            .ToHashSet();

        var missing = apiOperations.Except(CliApp.CoveredOperations).Order().ToArray();
        Assert.True(missing.Length == 0, $"API operations without a CLI command: {string.Join(", ", missing)}");
    }

    [Fact]
    public async Task Cli_does_not_claim_operations_the_api_does_not_have()
    {
        using var api = new ApiFixture();
        using var client = api.CreateClient();
        using var document = JsonDocument.Parse(await client.GetStringAsync("/openapi/v1.json"));

        var apiOperations = document.RootElement.GetProperty("paths").EnumerateObject()
            .SelectMany(path => path.Value.EnumerateObject())
            .Select(operation => operation.Value.GetProperty("operationId").GetString()!)
            .ToHashSet();

        var unknown = CliApp.CoveredOperations.Except(apiOperations).Order().ToArray();
        Assert.True(unknown.Length == 0, $"CLI commands for unknown API operations: {string.Join(", ", unknown)}");
    }
}
