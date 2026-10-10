using System.Text.Json;

namespace ProjectOverview.Cli;

public static class ApiAddress
{
    public const string EnvironmentVariable = "PROJECT_OVERVIEW_API_URL";
    public static readonly Uri Default = new("http://localhost:5180");

    /// <summary>Resolves the API address: explicit option, then environment variable, then config file, then default.</summary>
    public static Uri Resolve(string? explicitAddress, Func<string, string?> getEnvironmentVariable, string? configFilePath)
    {
        if (!string.IsNullOrWhiteSpace(explicitAddress))
            return Parse(explicitAddress, "--api-url");

        var fromEnvironment = getEnvironmentVariable(EnvironmentVariable);
        if (!string.IsNullOrWhiteSpace(fromEnvironment))
            return Parse(fromEnvironment, EnvironmentVariable);

        if (configFilePath is not null && File.Exists(configFilePath))
        {
            try
            {
                using var document = JsonDocument.Parse(File.ReadAllText(configFilePath));
                if (document.RootElement.TryGetProperty("apiUrl", out var apiUrl)
                    && apiUrl.ValueKind == JsonValueKind.String
                    && apiUrl.GetString() is { Length: > 0 } value)
                    return Parse(value, $"apiUrl in {configFilePath}");
            }
            catch (JsonException exception)
            {
                throw new ArgumentException($"Config file {configFilePath} is not valid JSON: {exception.Message}");
            }
        }

        return Default;
    }

    private static Uri Parse(string value, string source)
    {
        if (Uri.TryCreate(value, UriKind.Absolute, out var uri) && uri.Scheme is "http" or "https")
            return uri;
        throw new ArgumentException($"Invalid API address '{value}' from {source}.");
    }
}
