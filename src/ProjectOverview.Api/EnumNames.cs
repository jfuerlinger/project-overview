using System.Text.Json;

namespace ProjectOverview.Api;

public static class EnumNames
{
    /// <summary>The wire names (camelCase) of an enum's values, in declaration order.</summary>
    public static string[] CamelCase<TEnum>() where TEnum : struct, Enum =>
        Enum.GetNames<TEnum>().Select(name => JsonNamingPolicy.CamelCase.ConvertName(name)).ToArray();
}
