using ProjectOverview.Cli;

namespace ProjectOverview.Cli.Tests;

public class ApiAddressTests : IDisposable
{
    private readonly string _configFile = Path.Combine(Path.GetTempPath(), $"po-config-{Guid.NewGuid():N}.json");

    public void Dispose()
    {
        if (File.Exists(_configFile)) File.Delete(_configFile);
    }

    private static Func<string, string?> Env(string? apiUrl = null) =>
        name => name == "PROJECT_OVERVIEW_API_URL" ? apiUrl : null;

    [Fact]
    public void Defaults_to_localhost()
    {
        var address = ApiAddress.Resolve(explicitAddress: null, Env(), configFilePath: null);

        Assert.Equal(new Uri("http://localhost:5180"), address);
    }

    [Fact]
    public void Environment_variable_overrides_the_default()
    {
        var address = ApiAddress.Resolve(null, Env("http://example.test:9000"), null);

        Assert.Equal(new Uri("http://example.test:9000"), address);
    }

    [Fact]
    public void Config_file_is_used_when_no_environment_variable_is_set()
    {
        File.WriteAllText(_configFile, """{ "apiUrl": "http://from-config.test:7000" }""");

        var address = ApiAddress.Resolve(null, Env(), _configFile);

        Assert.Equal(new Uri("http://from-config.test:7000"), address);
    }

    [Fact]
    public void Environment_variable_beats_config_file_and_explicit_beats_both()
    {
        File.WriteAllText(_configFile, """{ "apiUrl": "http://from-config.test:7000" }""");

        Assert.Equal(
            new Uri("http://from-env.test:8000"),
            ApiAddress.Resolve(null, Env("http://from-env.test:8000"), _configFile));
        Assert.Equal(
            new Uri("http://explicit.test:6000"),
            ApiAddress.Resolve("http://explicit.test:6000", Env("http://from-env.test:8000"), _configFile));
    }

    [Theory]
    [InlineData("{ not json")]
    public void Malformed_config_file_is_reported_as_an_address_problem(string content)
    {
        File.WriteAllText(_configFile, content);

        var exception = Assert.Throws<ArgumentException>(() => ApiAddress.Resolve(null, Env(), _configFile));

        Assert.Contains(_configFile, exception.Message);
    }

    [Fact]
    public void Non_string_api_url_in_config_file_falls_back_to_the_default()
    {
        File.WriteAllText(_configFile, """{ "apiUrl": 5 }""");

        Assert.Equal(ApiAddress.Default, ApiAddress.Resolve(null, Env(), _configFile));
    }

    [Fact]
    public void Invalid_address_is_reported_with_its_source()
    {
        var exception = Assert.Throws<ArgumentException>(() => ApiAddress.Resolve(null, Env("not a url"), null));

        Assert.Contains("PROJECT_OVERVIEW_API_URL", exception.Message);
    }
}
