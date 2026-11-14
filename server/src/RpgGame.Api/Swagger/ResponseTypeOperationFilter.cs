using Microsoft.AspNetCore.Mvc;
using Microsoft.OpenApi;
using Swashbuckle.AspNetCore.SwaggerGen;

namespace RpgGame.Api.Swagger;

/// <summary>
/// Reads [ProducesResponseType] attributes directly from action methods and adds
/// response schemas to the OpenAPI operation. This supplements Swashbuckle's
/// built-in API Explorer integration which may not pick up all response types.
/// </summary>
public class ResponseTypeOperationFilter : IOperationFilter
{
    public void Apply(OpenApiOperation operation, OperationFilterContext context)
    {
        var attrs = context.MethodInfo
            .GetCustomAttributes(typeof(ProducesResponseTypeAttribute), inherit: true)
            .Cast<ProducesResponseTypeAttribute>()
            .Where(a => a.Type != null && a.Type != typeof(void));

        foreach (var attr in attrs)
        {
            operation.Responses ??= new OpenApiResponses();

            var statusCode = attr.StatusCode.ToString();
            var schema = context.SchemaGenerator.GenerateSchema(attr.Type!, context.SchemaRepository);

            if (!operation.Responses.TryGetValue(statusCode, out var existing)
                || existing is not OpenApiResponse response)
            {
                response = new OpenApiResponse { Description = existing?.Description ?? "Success" };
                operation.Responses[statusCode] = response;
            }

            response.Content ??= new Dictionary<string, OpenApiMediaType>();
            response.Content["application/json"] = new OpenApiMediaType { Schema = schema };
        }
    }
}
