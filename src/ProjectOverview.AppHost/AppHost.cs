var builder = DistributedApplication.CreateBuilder(args);

// The API listens on a fixed local port (see its launch settings) so the CLI has a stable default address.
var api = builder.AddProject<Projects.ProjectOverview_Api>("api");

// The "http-test" launch profile runs against a throwaway database in the temp directory, started empty
// on every run, so manual and browser tests never touch the real data in the default location.
if (string.Equals(builder.Configuration["ProjectOverview:UseTestDatabase"], "true", StringComparison.OrdinalIgnoreCase))
{
    var directory = Path.Combine(Path.GetTempPath(), "project-overview-test");
    Directory.CreateDirectory(directory);
    var databasePath = Path.Combine(directory, "project-overview.db");
    foreach (var file in Directory.GetFiles(directory, "project-overview.db*"))
        File.Delete(file);

    api.WithEnvironment("ConnectionStrings__Default", $"Data Source={databasePath}");
}

builder.AddProject<Projects.ProjectOverview_Web>("web")
    .WithReference(api)
    .WaitFor(api)
    .WithExternalHttpEndpoints();

builder.Build().Run();
