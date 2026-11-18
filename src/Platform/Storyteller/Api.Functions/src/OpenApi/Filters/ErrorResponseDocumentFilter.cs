using _42.Platform.Storyteller.Api.Models;

using Microsoft.Azure.WebJobs.Extensions.OpenApi.Core.Abstractions;
using Microsoft.OpenApi.Models;

namespace _42.Platform.Storyteller.Api.OpenApi.Filters;

// The generator describes ErrorResponse.Details (an object? in C#) as a JSON object, but some errors answer with an
// array: InvalidSettings lists every problem of the project settings. A schema without a type is any JSON value in
// OpenAPI 3.0, so both SDKs keep an object and an array alike (object in C#, unknown in TypeScript).
public class ErrorResponseDocumentFilter : IDocumentFilter
{
    public const string DetailsDescription = "Structured details of some errors, any JSON value: an object, such as the Version of SettingsPreconditionFailed or the Format, Rule and Path of format.<rule>, or an array, such as the problems of InvalidSettings, each with its Path, Rule and Message.";

    public void Apply(IHttpRequestDataObject req, OpenApiDocument document)
    {
        if (document.Components?.Schemas is null
            || !document.Components.Schemas.TryGetValue(nameof(ErrorResponse), out var schema)
            || schema.Properties is null
            || !schema.Properties.ContainsKey(nameof(ErrorResponse.Details)))
        {
            return;
        }

        schema.Properties[nameof(ErrorResponse.Details)] = new OpenApiSchema { Description = DetailsDescription };
    }
}
