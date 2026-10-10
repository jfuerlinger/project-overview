using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.EntityFrameworkCore;
using ProjectOverview.Api;
using ProjectOverview.Api.Projects;
using ProjectOverview.Api.WorkPackages;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddDbContext<ProjectOverviewDbContext>((services, options) =>
{
    var connectionString = services.GetRequiredService<IConfiguration>().GetConnectionString("Default")
        ?? $"Data Source={DefaultDatabasePath()}";
    options.UseSqlite(connectionString);
});

builder.Services.ConfigureHttpJsonOptions(options =>
{
    options.SerializerOptions.Converters.Add(new JsonStringEnumConverter(JsonNamingPolicy.CamelCase, allowIntegerValues: false));
    // The web defaults accept numbers written as strings, which the OpenAPI document then describes as
    // "integer or string" and generated clients cannot use.
    options.SerializerOptions.NumberHandling = System.Text.Json.Serialization.JsonNumberHandling.Strict;
});
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
app.MapWorkPackageEndpoints();

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
