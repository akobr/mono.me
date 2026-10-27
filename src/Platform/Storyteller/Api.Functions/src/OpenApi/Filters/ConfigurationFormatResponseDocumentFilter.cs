using _42.Platform.Storyteller.ConfigurationFormats;

using Microsoft.Azure.WebJobs.Extensions.OpenApi.Core.Abstractions;
using Microsoft.OpenApi.Any;
using Microsoft.OpenApi.Models;

using Newtonsoft.Json.Linq;

namespace _42.Platform.Storyteller.Api.OpenApi.Filters;

// The bodies of ?format= on the effective and resolved reads: properties and env as text/plain and yaml as
// application/yaml, next to the Configuration model of application/json, which flat shares as a JSON object of strings
// (the description of the 200 says so). The 304 stays without content. The examples are the formatter's own output.
public class ConfigurationFormatResponseDocumentFilter : IDocumentFilter
{
    private static readonly HashSet<string> FormattedOperationIds =
    [
        Definitions.RouteIds.Configuration.GetConfiguration,
        Definitions.RouteIds.Configuration.GetResolvedConfiguration,
    ];

    private static readonly JObject ExampleDocument = JObject.Parse("""{"db":{"hosts":["a","b"],"port":5432},"features":["vat"]}""");

    public void Apply(IHttpRequestDataObject req, OpenApiDocument document)
    {
        foreach (var operation in document.Paths.Values.SelectMany(path => path.Operations.Values))
        {
            if (operation.OperationId is null
                || !FormattedOperationIds.Contains(operation.OperationId)
                || !operation.Responses.TryGetValue("200", out var response))
            {
                continue;
            }

            response.Content[Definitions.ContentTypes.PlainText] = new OpenApiMediaType
            {
                Schema = new OpenApiSchema { Type = "string" },
                Examples = new Dictionary<string, OpenApiExample>
                {
                    [ConfigurationFormat.Properties.GetName()] = Example(ConfigurationFormat.Properties),
                    [ConfigurationFormat.Env.GetName()] = Example(ConfigurationFormat.Env),
                },
            };
            response.Content[Definitions.ContentTypes.Yaml] = new OpenApiMediaType
            {
                Schema = new OpenApiSchema { Type = "string" },
                Examples = new Dictionary<string, OpenApiExample>
                {
                    [ConfigurationFormat.Yaml.GetName()] = Example(ConfigurationFormat.Yaml),
                },
            };
        }
    }

    private static OpenApiExample Example(ConfigurationFormat format)
    {
        return new OpenApiExample
        {
            Summary = $"format={format.GetName()}",
            Value = new OpenApiString(ConfigurationFormatter.Format(ExampleDocument, format).Text),
        };
    }
}
