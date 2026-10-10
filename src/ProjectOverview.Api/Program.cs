using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.EntityFrameworkCore;
using ProjectOverview.Api;
using ProjectOverview.Api.Projects;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddDbContext<ProjectOverviewDbContext>((services, options) =>
{
    var connectionString = services.GetRequiredService<IConfiguration>().GetConnectionString("Default")
        ?? $"Data Source={DefaultDatabasePath()}";
    options.UseSqlite(connectionString);
});

builder.Services.ConfigureHttpJsonOptions(options =>
    options.SerializerOptions.Converters.Add(new JsonStringEnumConverter(JsonNamingPolicy.CamelCase)));
// NSwag (used to generate the CLI client) does not read OpenAPI 3.1 reliably.
builder.Services.AddOpenApi(options =>
{
    options.OpenApiVersion = Microsoft.OpenApi.OpenApiSpecVersion.OpenApi3_0;
    options.AddProblemDetailsStatusAsInteger();
});

var app = builder.Build();

using (var scope = app.Services.CreateScope())
{
    scope.ServiceProvider.GetRequiredService<ProjectOverviewDbContext>().Database.Migrate();
}

app.MapOpenApi();
app.MapProjectEndpoints();

app.Run();

public partial class Program
{
    // A fixed per-user location, so the data does not depend on the working directory the API is started from.
    private static string DefaultDatabasePath()
    {
        var directory = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "project-overview");
        Directory.CreateDirectory(directory);
        return Path.Combine(directory, "project-overview.db");
    }
}
