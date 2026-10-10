using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.OpenApi;
using Microsoft.OpenApi;

namespace ProjectOverview.Api;

/// <summary>
/// ASP.NET Core documents ProblemDetails.status as "integer or string" (pattern + anyOf), which
/// client generators turn into an unusable type. The API only ever sends an integer.
/// </summary>
internal static class OpenApiProblemDetailsStatusTransformer
{
    public static OpenApiOptions AddProblemDetailsStatusAsInteger(this OpenApiOptions options) =>
        options.AddSchemaTransformer((schema, context, _) =>
        {
            var property = context.JsonPropertyInfo;
            if (property is { Name: "status" } && typeof(ProblemDetails).IsAssignableFrom(property.DeclaringType))
            {
                schema.Type = JsonSchemaType.Integer;
                schema.Format = "int32";
                schema.Pattern = null;
                schema.AnyOf = null;
            }
            return Task.CompletedTask;
        });
}
