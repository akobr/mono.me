using Microsoft.Azure.WebJobs.Extensions.OpenApi.Core.Abstractions;
using Microsoft.OpenApi.Models;

namespace _42.Platform.Storyteller.Api.OpenApi.Filters;

// The OpenAPI generator describes System.DateOnly as an object with Year, Month and Day, while the API serializes it as
// a date string ("2026-09-01"). The generated clients would build a class for the object and fail on the string, so the
// shared DateOnly schema becomes a string with the date format; the $refs of the properties stay as they are.
public class DateOnlySchemaDocumentFilter : IDocumentFilter
{
    public const string SchemaName = "DateOnly";

    public void Apply(IHttpRequestDataObject req, OpenApiDocument document)
    {
        if (document.Components?.Schemas is null || !document.Components.Schemas.ContainsKey(SchemaName))
        {
            return;
        }

        document.Components.Schemas[SchemaName] = new OpenApiSchema
        {
            Type = "string",
            Format = "date",
            Description = "A calendar day, yyyy-MM-dd.",
        };
    }
}
