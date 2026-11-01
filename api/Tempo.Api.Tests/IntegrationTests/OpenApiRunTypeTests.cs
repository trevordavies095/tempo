using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.OpenApi;
using Swashbuckle.AspNetCore.Swagger;
using Tempo.Api.Models;
using Tempo.Api.Tests.Infrastructure;
using Xunit;

namespace Tempo.Api.Tests.IntegrationTests;

/// <summary>
/// The OpenAPI document publishes <see cref="RunTypes.All"/> as the runType enum.
/// </summary>
[Collection("Integration Tests")]
public class OpenApiRunTypeTests : IClassFixture<TempoWebApplicationFactory>
{
    private readonly TempoWebApplicationFactory _factory;

    public OpenApiRunTypeTests(TempoWebApplicationFactory factory)
    {
        _factory = factory;
    }

    private OpenApiDocument GetDocument() =>
        _factory.Services.GetRequiredService<ISwaggerProvider>().GetSwagger("v1");

    private static IEnumerable<string?> EnumValues(IOpenApiSchema? schema) =>
        schema?.Enum?.Select(node => node?.GetValue<string>()) ?? [];

    [Fact]
    public void ListWorkouts_RunTypeFilter_PublishesRunTypesEnum()
    {
        var operation = GetDocument().Paths["/workouts"].Operations![HttpMethod.Get];
        var parameter = operation.Parameters!.Single(p => p.Name == "runType");

        EnumValues(parameter.Schema).Should().Equal(RunTypes.All);
    }

    [Fact]
    public void UpdateWorkout_RequestBody_ReferencesUpdateWorkoutRequest()
    {
        var operation = GetDocument().Paths["/workouts/{id}"].Operations![HttpMethod.Patch];
        var bodySchema = operation.RequestBody!.Content!["application/json"].Schema;

        bodySchema.Should().BeOfType<OpenApiSchemaReference>()
            .Which.Reference.Id.Should().Be(nameof(UpdateWorkoutRequest));
    }

    [Fact]
    public void UpdateWorkoutRequest_RunType_PublishesNullableRunTypesEnum()
    {
        var schema = GetDocument().Components!.Schemas![nameof(UpdateWorkoutRequest)];
        var runType = schema.Properties!["runType"];

        EnumValues(runType).Should().Equal(RunTypes.All);
        runType.Type.Should().HaveFlag(JsonSchemaType.Null);
    }
}
