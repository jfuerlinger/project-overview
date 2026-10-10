var builder = DistributedApplication.CreateBuilder(args);

// The API listens on a fixed local port (see its launch settings) so the CLI has a stable default address.
var api = builder.AddProject<Projects.ProjectOverview_Api>("api");

builder.AddProject<Projects.ProjectOverview_Web>("web")
    .WithReference(api)
    .WaitFor(api)
    .WithExternalHttpEndpoints();

builder.Build().Run();
