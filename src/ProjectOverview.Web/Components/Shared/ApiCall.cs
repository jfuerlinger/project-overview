using ProjectOverview.Client;
using ValidationProblem = ProjectOverview.Client.HttpValidationProblemDetails;

namespace ProjectOverview.Web.Components.Shared;

public static class ApiCall
{
    /// <summary>Runs an API call and returns German error messages, or an empty list on success.</summary>
    public static async Task<List<string>> Run(Func<Task> call)
    {
        try
        {
            await call();
            return [];
        }
        catch (ApiException<ValidationProblem> exception)
        {
            var messages = ApiErrorMessages.From(exception.Result);
            if (messages.Count == 0) messages.Add("Die API hat die Eingabe abgelehnt.");
            return messages;
        }
        catch (ApiException exception) when (exception.StatusCode == 404)
        {
            return ["Der Eintrag existiert nicht mehr."];
        }
        catch (ApiException exception)
        {
            return [exception.Message];
        }
        catch (HttpRequestException)
        {
            return ["Die API ist nicht erreichbar."];
        }
        catch (TaskCanceledException)
        {
            return ["Die API hat nicht rechtzeitig geantwortet."];
        }
    }

    public static string StatusLabel(string status) => status switch
    {
        "Open" or "open" => "Offen",
        "InProgress" or "inProgress" => "In Arbeit",
        "Done" or "done" => "Erledigt",
        "Discarded" or "discarded" => "Verworfen",
        _ => status,
    };
}
