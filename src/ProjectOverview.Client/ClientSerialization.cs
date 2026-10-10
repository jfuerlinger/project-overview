using System.Text.Json;
using System.Text.Json.Serialization;

namespace ProjectOverview.Client;

public partial class ProjectOverviewClient
{
    // The API sends enums as camelCase strings; the generated client would otherwise expect numbers.
    static partial void UpdateJsonSerializerSettings(JsonSerializerOptions settings) =>
        settings.Converters.Add(new JsonStringEnumConverter(JsonNamingPolicy.CamelCase));
}
