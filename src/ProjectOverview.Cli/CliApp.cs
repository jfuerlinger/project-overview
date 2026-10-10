using System.CommandLine;
using System.Text.Json;
using ProjectOverview.Client;

namespace ProjectOverview.Cli;

/// <summary>
/// The CLI is a pure API client (ADR-0003). Every command declares the OpenAPI operation it covers,
/// which lets a test enforce that no API operation is left without a command.
/// </summary>
public static class CliApp
{
    private static readonly JsonSerializerOptions JsonOutput = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true,
    };

    /// <summary>The OpenAPI operation ids that have a CLI command.</summary>
    public static IReadOnlyCollection<string> CoveredOperations { get; } =
        new CommandRegistry(context: null).Operations;

    public static async Task<int> RunAsync(string[] args, CliEnvironment environment)
    {
        var registry = new CommandRegistry(new RunContext(environment));
        var parseResult = registry.Root.Parse(args);
        return await parseResult.InvokeAsync(new InvocationConfiguration
        {
            Output = environment.Out,
            Error = environment.Error,
        });
    }

    private sealed record RunContext(CliEnvironment Environment);

    /// <summary>A failure the CLI itself detects (not the API); reported like API errors.</summary>
    private sealed class CliFailure(int exitCode, string code, string message) : Exception(message)
    {
        public int ExitCode { get; } = exitCode;
        public string Code { get; } = code;
    }

    private sealed class CommandRegistry
    {
        private readonly RunContext? _context;
        private readonly HashSet<string> _operations = [];
        private readonly Option<bool> _json = new("--json") { Description = "Machine-readable JSON output.", Recursive = true };
        private readonly Option<string?> _apiUrl = new("--api-url")
        {
            Description = $"API address (default: {ApiAddress.Default}, or ${ApiAddress.EnvironmentVariable}).",
            Recursive = true,
        };

        public RootCommand Root { get; }
        public IReadOnlyCollection<string> Operations => _operations;

        public CommandRegistry(RunContext? context)
        {
            _context = context;
            Root = new RootCommand("Manage projects, work packages, milestones and their dependencies.");
            Root.Options.Add(_json);
            Root.Options.Add(_apiUrl);

            var projects = new Command("projects", "Manage projects.");
            projects.Subcommands.Add(ListProjectsCommand());
            projects.Subcommands.Add(CreateProjectCommand());
            projects.Subcommands.Add(GetProjectCommand());
            projects.Subcommands.Add(UpdateProjectCommand());
            projects.Subcommands.Add(SetStatusCommand());
            projects.Subcommands.Add(DeleteProjectCommand());
            Root.Subcommands.Add(projects);
        }

        private Command ListProjectsCommand()
        {
            var command = new Command("list", "List all projects.");
            Cover("listProjects");
            command.SetAction((parseResult, ct) => Execute(parseResult, async client =>
            {
                var projects = (await client.ListProjectsAsync(ct)).Select(ProjectOutput.From).ToArray();
                if (parseResult.GetValue(_json)) WriteJson(projects);
                else WriteTable(projects);
            }));
            return command;
        }

        private Command CreateProjectCommand()
        {
            var name = new Option<string>("--name") { Description = "Project name.", Required = true };
            var description = new Option<string?>("--description") { Description = "Project description." };
            var startDate = DateOption("--start-date", "Start date (yyyy-MM-dd).");
            var targetDate = DateOption("--target-date", "Target date (yyyy-MM-dd).");

            var command = new Command("create", "Create a project.")
            {
                name, description, startDate, targetDate,
            };
            Cover("createProject");
            command.SetAction((parseResult, ct) => Execute(parseResult, async client =>
            {
                var created = await client.CreateProjectAsync(new CreateProjectRequest
                {
                    Name = parseResult.GetValue(name),
                    Description = parseResult.GetValue(description),
                    StartDate = parseResult.GetValue(startDate),
                    TargetDate = parseResult.GetValue(targetDate),
                }, ct);
                var output = ProjectOutput.From(created);
                if (parseResult.GetValue(_json)) WriteJson(output);
                else WriteTable([output]);
            }));
            return command;
        }

        private Command GetProjectCommand()
        {
            var id = new Argument<Guid>("id") { Description = "Project id." };
            var command = new Command("get", "Show one project.") { id };
            Cover("getProject");
            command.SetAction((parseResult, ct) => Execute(parseResult, async client =>
            {
                var project = ProjectOutput.From(await client.GetProjectAsync(parseResult.GetValue(id), ct));
                if (parseResult.GetValue(_json)) WriteJson(project);
                else WriteTable([project]);
            }));
            return command;
        }

        private Command UpdateProjectCommand()
        {
            var id = new Argument<Guid>("id") { Description = "Project id." };
            var name = new Option<string?>("--name") { Description = "New name." };
            var description = new Option<string?>("--description") { Description = "New description; pass \"\" to clear it." };
            var startDate = DateOption("--start-date", "New start date (yyyy-MM-dd).");
            var targetDate = DateOption("--target-date", "New target date (yyyy-MM-dd).");
            var clearStartDate = new Option<bool>("--clear-start-date") { Description = "Remove the start date." };
            var clearTargetDate = new Option<bool>("--clear-target-date") { Description = "Remove the target date." };

            var command = new Command("update", "Change name, description or dates of a project. Omitted fields stay as they are.")
            {
                id, name, description, startDate, targetDate, clearStartDate, clearTargetDate,
            };
            command.Validators.Add(result =>
            {
                if (result.GetResult(startDate) is { Implicit: false } && result.GetResult(clearStartDate) is { Implicit: false })
                    result.AddError("--start-date and --clear-start-date cannot be combined.");
                if (result.GetResult(targetDate) is { Implicit: false } && result.GetResult(clearTargetDate) is { Implicit: false })
                    result.AddError("--target-date and --clear-target-date cannot be combined.");
            });
            Cover("updateProject");
            command.SetAction((parseResult, ct) => Execute(parseResult, async client =>
            {
                bool Given(Option option) => parseResult.GetResult(option) is { Implicit: false };
                if (!new Option[] { name, description, startDate, targetDate, clearStartDate, clearTargetDate }.Any(Given))
                    throw new CliFailure(1, "nothing_to_update", "Pass at least one option to change.");

                // The API replaces all fields, so start from the current values and overlay what was given.
                var projectId = parseResult.GetValue(id);
                var current = await client.GetProjectAsync(projectId, ct);
                var updated = await client.UpdateProjectAsync(projectId, new UpdateProjectRequest
                {
                    Name = Given(name) ? parseResult.GetValue(name) : current.Name,
                    Description = Given(description) ? parseResult.GetValue(description) : current.Description,
                    StartDate = Given(clearStartDate) ? null : Given(startDate) ? parseResult.GetValue(startDate) : current.StartDate,
                    TargetDate = Given(clearTargetDate) ? null : Given(targetDate) ? parseResult.GetValue(targetDate) : current.TargetDate,
                }, ct);

                var output = ProjectOutput.From(updated);
                if (parseResult.GetValue(_json)) WriteJson(output);
                else WriteTable([output]);
            }));
            return command;
        }

        private Command SetStatusCommand()
        {
            var id = new Argument<Guid>("id") { Description = "Project id." };
            var status = new Argument<string>("status") { Description = "New status." };
            status.AcceptOnlyFromAmong(
                Enum.GetNames<ProjectStatus>().Select(name => JsonNamingPolicy.CamelCase.ConvertName(name)).ToArray());

            var command = new Command("set-status", "Set the status of a project.") { id, status };
            Cover("setProjectStatus");
            command.SetAction((parseResult, ct) => Execute(parseResult, async client =>
            {
                var updated = await client.SetProjectStatusAsync(parseResult.GetValue(id), new SetProjectStatusRequest
                {
                    Status = Enum.Parse<ProjectStatus>(parseResult.GetValue(status)!, ignoreCase: true),
                }, ct);
                var output = ProjectOutput.From(updated);
                if (parseResult.GetValue(_json)) WriteJson(output);
                else WriteTable([output]);
            }));
            return command;
        }

        private Command DeleteProjectCommand()
        {
            var id = new Argument<Guid>("id") { Description = "Project id." };
            var yes = new Option<bool>("--yes", "-y") { Description = "Delete without asking for confirmation." };

            var command = new Command("delete", "Delete a project.") { id, yes };
            Cover("deleteProject");
            command.SetAction((parseResult, ct) => Execute(parseResult, async client =>
            {
                var projectId = parseResult.GetValue(id);

                // The lookup is only needed to name the project in the prompt.
                if (!parseResult.GetValue(yes))
                {
                    var project = await client.GetProjectAsync(projectId, ct);
                    if (!Confirm($"Delete project '{project.Name}' ({project.Id})? [y/N] "))
                        throw new CliFailure(1, "confirmation_required", "Deletion not confirmed. Pass --yes to delete without asking.");
                }

                await client.DeleteProjectAsync(projectId, ct);
                if (parseResult.GetValue(_json)) WriteJson(new { deleted = projectId });
                else _context!.Environment.Out.WriteLine($"Deleted {projectId}");
            }));
            return command;
        }

        private bool Confirm(string question)
        {
            var environment = _context!.Environment;
            environment.Error.Write(question);
            var answer = environment.ReadLine()?.Trim();
            environment.Error.WriteLine();
            return answer is not null && answer.ToLowerInvariant() is "y" or "yes" or "j" or "ja";
        }

        private void Cover(string operationId) => _operations.Add(operationId);

        private static Option<DateOnly?> DateOption(string name, string description) => new(name)
        {
            Description = description,
            CustomParser = result =>
            {
                var token = result.Tokens.SingleOrDefault()?.Value;
                if (token is null) return null;
                if (DateOnly.TryParseExact(token, "yyyy-MM-dd", out var date)) return date;
                result.AddError($"'{token}' is not a date in the format yyyy-MM-dd.");
                return null;
            },
        };

        /// <summary>Runs an API call and maps failures to exit codes: 1 = API rejected, 2 = API not reachable or misconfigured.</summary>
        private async Task<int> Execute(ParseResult parseResult, Func<ProjectOverviewClient, Task> action)
        {
            var environment = _context!.Environment;
            try
            {
                var address = ApiAddress.Resolve(
                    parseResult.GetValue(_apiUrl), environment.GetEnvironmentVariable, environment.ConfigFilePath);
                var client = new ProjectOverviewClient(environment.CreateHttpClient(address));
                await action(client);
                return 0;
            }
            catch (CliFailure failure)
            {
                return Fail(failure.ExitCode, new { code = failure.Code, title = failure.Message });
            }
            catch (ApiException<HttpValidationProblemDetails> exception)
            {
                var problem = exception.Result;
                return Fail(1, new
                {
                    code = "api_error",
                    status = exception.StatusCode,
                    title = problem?.Title ?? exception.Message,
                    errors = problem?.Errors,
                });
            }
            catch (ApiException exception)
            {
                return Fail(1, new { code = "api_error", status = exception.StatusCode, title = exception.Message });
            }
            catch (HttpRequestException exception)
            {
                return Fail(2, new { code = "api_unreachable", title = exception.Message });
            }
            catch (TaskCanceledException)
            {
                return Fail(2, new { code = "api_timeout", title = "The API did not respond in time." });
            }
            catch (ArgumentException exception)
            {
                return Fail(2, new { code = "invalid_api_address", title = exception.Message });
            }
        }

        private int Fail(int exitCode, object error)
        {
            _context!.Environment.Error.WriteLine(JsonSerializer.Serialize(new { error }, JsonOutput));
            return exitCode;
        }

        private void WriteJson<T>(T value) =>
            _context!.Environment.Out.WriteLine(JsonSerializer.Serialize(value, JsonOutput));

        private void WriteTable(IReadOnlyList<ProjectOutput> projects)
        {
            string[] header = ["ID", "NAME", "STATUS", "START", "TARGET"];
            var rows = projects
                .Select(p => new[] { p.Id.ToString(), p.Name, p.Status, p.StartDate?.ToString("yyyy-MM-dd") ?? "-", p.TargetDate?.ToString("yyyy-MM-dd") ?? "-" })
                .Prepend(header)
                .ToArray();
            var widths = header.Select((_, column) => rows.Max(row => row[column].Length)).ToArray();

            foreach (var row in rows)
            {
                var line = string.Join("  ", row.Select((cell, column) => cell.PadRight(widths[column])));
                _context!.Environment.Out.WriteLine(line.TrimEnd());
            }
        }
    }

    /// <summary>The CLI's JSON contract for a project; independent of the generated client's enum casing.</summary>
    private sealed record ProjectOutput(
        Guid Id, string Name, string? Description, DateOnly? StartDate, DateOnly? TargetDate, string Status)
    {
        public static ProjectOutput From(ProjectResponse project) => new(
            project.Id, project.Name, project.Description, project.StartDate, project.TargetDate,
            char.ToLowerInvariant(project.Status.ToString()[0]) + project.Status.ToString()[1..]);
    }
}
