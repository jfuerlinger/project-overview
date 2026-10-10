using Microsoft.AspNetCore.Http.HttpResults;

namespace ProjectOverview.Api;

/// <summary>
/// Collects validation problems. Each problem has an English message (for CLI users and agents) and a
/// stable code (for clients that localize, such as the web UI). Both are returned side by side:
/// <c>errors</c> holds the messages, <c>errorCodes</c> the codes, in the same order per field.
/// </summary>
public sealed class ValidationErrors
{
    private readonly Dictionary<string, List<string>> _messages = [];
    private readonly Dictionary<string, List<string>> _codes = [];

    public bool Any => _messages.Count > 0;

    public ValidationErrors Add(string field, string code, string message)
    {
        if (!_messages.TryGetValue(field, out var messages))
        {
            _messages[field] = messages = [];
            _codes[field] = [];
        }
        messages.Add(message);
        _codes[field].Add(code);
        return this;
    }

    public ValidationProblem ToProblem() => TypedResults.ValidationProblem(
        _messages.ToDictionary(entry => entry.Key, entry => entry.Value.ToArray()),
        extensions: new Dictionary<string, object?>
        {
            ["errorCodes"] = _codes.ToDictionary(entry => entry.Key, entry => entry.Value.ToArray()),
        });
}
