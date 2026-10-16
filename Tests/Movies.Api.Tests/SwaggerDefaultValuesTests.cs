using Microsoft.AspNetCore.Mvc.Abstractions;
using Microsoft.AspNetCore.Mvc.ApiExplorer;
using Microsoft.AspNetCore.Mvc.ModelBinding;
using Microsoft.OpenApi.Any;
using Microsoft.OpenApi.Models;
using Movies.Api.Swagger;
using Swashbuckle.AspNetCore.SwaggerGen;

namespace Movies.Api.Tests;

public class SwaggerDefaultValuesTests
{
    private static OperationFilterContext ContextFor(ApiDescription description) =>
        new(description, null!, new SchemaRepository(), typeof(object).GetMethod(nameof(ToString))!);

    private static ApiDescription Description() => new() { ActionDescriptor = new ActionDescriptor() };

    [Fact]
    public void content_types_the_action_does_not_produce_are_removed()
    {
        // Arrange
        var description = Description();
        description.SupportedResponseTypes.Add(new ApiResponseType
        {
            StatusCode = 200,
            ApiResponseFormats = { new ApiResponseFormat { MediaType = "application/json" } }
        });
        var operation = new OpenApiOperation
        {
            Responses = new OpenApiResponses
            {
                ["200"] = new OpenApiResponse
                {
                    Content =
                    {
                        ["application/json"] = new OpenApiMediaType(),
                        ["text/plain"] = new OpenApiMediaType()
                    }
                }
            }
        };

        // Act
        new SwaggerDefaultValues().Apply(operation, ContextFor(description));

        // Assert
        Assert.Equal(["application/json"], operation.Responses["200"].Content.Keys);
    }

    [Fact]
    public void default_responses_are_keyed_as_default()
    {
        // Arrange
        var description = Description();
        description.SupportedResponseTypes.Add(new ApiResponseType
        {
            IsDefaultResponse = true,
            ApiResponseFormats = { new ApiResponseFormat { MediaType = "application/problem+json" } }
        });
        var operation = new OpenApiOperation
        {
            Responses = new OpenApiResponses
            {
                ["default"] = new OpenApiResponse
                {
                    Content = { ["application/problem+json"] = new OpenApiMediaType(), ["text/plain"] = new OpenApiMediaType() }
                }
            }
        };

        // Act
        new SwaggerDefaultValues().Apply(operation, ContextFor(description));

        // Assert
        Assert.Equal(["application/problem+json"], operation.Responses["default"].Content.Keys);
    }

    [Fact]
    public void operation_without_parameters_is_left_alone()
    {
        // Arrange
        var operation = new OpenApiOperation { Parameters = null };

        // Act
        var exception = Record.Exception(() => new SwaggerDefaultValues().Apply(operation, ContextFor(Description())));

        // Assert
        Assert.Null(exception);
    }

    [Fact]
    public void parameters_pick_up_the_default_value_and_required_flag()
    {
        // Arrange
        var metadata = new EmptyModelMetadataProvider().GetMetadataForType(typeof(int));
        var description = Description();
        description.ParameterDescriptions.Add(new ApiParameterDescription
        {
            Name = "page",
            ModelMetadata = metadata,
            DefaultValue = 1,
            IsRequired = true
        });
        var operation = new OpenApiOperation
        {
            Parameters = [new OpenApiParameter { Name = "page", Schema = new OpenApiSchema() }]
        };

        // Act
        new SwaggerDefaultValues().Apply(operation, ContextFor(description));

        // Assert
        var parameter = operation.Parameters[0];
        Assert.True(parameter.Required);
        Assert.Equal(1, Assert.IsType<OpenApiInteger>(parameter.Schema.Default).Value);
    }

    [Fact]
    public void an_existing_schema_default_is_not_overwritten()
    {
        // Arrange
        var metadata = new EmptyModelMetadataProvider().GetMetadataForType(typeof(int));
        var description = Description();
        description.ParameterDescriptions.Add(new ApiParameterDescription
        {
            Name = "page",
            ModelMetadata = metadata,
            DefaultValue = 1
        });
        var operation = new OpenApiOperation
        {
            Parameters = [new OpenApiParameter { Name = "page", Schema = new OpenApiSchema { Default = new OpenApiInteger(5) } }]
        };

        // Act
        new SwaggerDefaultValues().Apply(operation, ContextFor(description));

        // Assert
        Assert.Equal(5, Assert.IsType<OpenApiInteger>(operation.Parameters[0].Schema.Default).Value);
    }
}
