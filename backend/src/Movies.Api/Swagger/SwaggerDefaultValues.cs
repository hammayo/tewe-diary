using System.Text.Json;
using Microsoft.AspNetCore.Mvc.ApiExplorer;
using Microsoft.OpenApi.Models;
using Swashbuckle.AspNetCore.SwaggerGen;

namespace Movies.Api.Swagger;

public sealed class SwaggerDefaultValues : IOperationFilter
{
    public void Apply(OpenApiOperation operation, OperationFilterContext context)
    {
        var apiDescription = context.ApiDescription;

        operation.Deprecated |= apiDescription.IsDeprecated();

        foreach (var responseType in context.ApiDescription.SupportedResponseTypes)
        {
            var responseKey = responseType.IsDefaultResponse
                ? "default"
                : responseType.StatusCode.ToString();
            var response = operation.Responses[responseKey];

            foreach (var contentType in response.Content.Keys
                             .Where(c => responseType.ApiResponseFormats.All(x => x.MediaType != c)))
            {
                response.Content.Remove(contentType);
            }
        }

        if (operation.Parameters == null)
        {
            return;
        }

        foreach (var parameter in operation.Parameters)
        {
            var description = apiDescription.ParameterDescriptions
                .First(p => p.Name == parameter.Name);

            parameter.Description ??= description.ModelMetadata?.Description;

            // Minimal API parameters report DBNull for "no default" and may have no model metadata.
            if (parameter.Schema.Default == null && description.DefaultValue is not (null or DBNull)
                && description.ModelMetadata is not null)
            {
                var json = JsonSerializer.Serialize(
                    description.DefaultValue,
                    description.ModelMetadata.ModelType);
                parameter.Schema.Default = OpenApiAnyFactory.CreateFromJson(json);
            }

            parameter.Required |= description.IsRequired;
        }
    }
}
