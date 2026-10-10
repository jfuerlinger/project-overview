using System.Globalization;
using System.Text.Json;
using ValidationProblem = ProjectOverview.Client.HttpValidationProblemDetails;

namespace ProjectOverview.Web.Components.Shared;

/// <summary>
/// Turns the API's validation problems into German messages. The API sends a stable code next to
/// each English message (<c>errorCodes</c>); unknown codes fall back to the English message.
/// </summary>
public static class ApiErrorMessages
{
    private static readonly Dictionary<string, string> FieldLabels = new()
    {
        ["name"] = "Der Name",
        ["description"] = "Die Beschreibung",
        ["targetDate"] = "Das Zieldatum",
        ["endDate"] = "Das Enddatum",
        ["status"] = "Der Status",
        ["title"] = "Der Titel",
        ["durationDays"] = "Die Dauer",
        ["milestoneId"] = "Der Meilenstein",
    };

    public static List<string> From(ValidationProblem? problem)
    {
        var messages = new List<string>();
        if (problem?.Errors is null) return messages;

        var codes = ReadCodes(problem);
        foreach (var (field, englishMessages) in problem.Errors)
        {
            var englishList = englishMessages.ToList();
            for (var index = 0; index < englishList.Count; index++)
            {
                var code = codes.TryGetValue(field, out var fieldCodes) && index < fieldCodes.Count ? fieldCodes[index] : null;
                messages.Add(Translate(field, code) ?? englishList[index]);
            }
        }
        return messages;
    }

    private static string? Translate(string field, string? code)
    {
        if (code is null) return null;
        var label = FieldLabels.GetValueOrDefault(field, field);
        var separator = code.IndexOf(':');
        var name = separator < 0 ? code : code[..separator];
        var argument = separator < 0 ? null : code[(separator + 1)..];

        return name switch
        {
            "required" => $"{label} ist erforderlich.",
            "max_length" when argument is not null => $"{label} darf höchstens {argument} Zeichen lang sein.",
            "before_start_date" => $"{label} darf nicht vor dem Startdatum liegen.",
            "not_found" => $"{label} existiert nicht.",
            "other_project" => $"{label} gehört zu einem anderen Projekt.",
            "min_value" when argument is not null => $"{label} muss mindestens {argument} betragen.",
            "max_value" when argument is not null => $"{label} darf höchstens {argument} betragen.",
            _ => null,
        };
    }

    private static Dictionary<string, List<string>> ReadCodes(ValidationProblem problem)
    {
        var result = new Dictionary<string, List<string>>();
        if (problem.AdditionalProperties is null
            || !problem.AdditionalProperties.TryGetValue("errorCodes", out var raw)
            || raw is not JsonElement { ValueKind: JsonValueKind.Object } element)
            return result;

        foreach (var property in element.EnumerateObject())
        {
            if (property.Value.ValueKind != JsonValueKind.Array) continue;
            result[property.Name] = property.Value.EnumerateArray()
                .Select(code => code.GetString() ?? "")
                .ToList();
        }
        return result;
    }
}
