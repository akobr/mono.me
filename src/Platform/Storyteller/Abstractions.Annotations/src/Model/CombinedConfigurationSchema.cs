using System.Collections.Generic;
using Newtonsoft.Json.Linq;

namespace _42.Platform.Storyteller;

public record class CombinedConfigurationSchema
{
    public required string AnnotationKey { get; init; }

    public required JObject MergedContent { get; init; }

    public required IReadOnlyList<ConfigurationSchema> AppliedSchemas { get; init; }
}
