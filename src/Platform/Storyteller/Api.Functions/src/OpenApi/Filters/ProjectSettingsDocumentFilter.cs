using _42.Platform.Storyteller.Api.Models;

using Microsoft.Azure.WebJobs.Extensions.OpenApi.Core.Abstractions;
using Microsoft.OpenApi.Models;

namespace _42.Platform.Storyteller.Api.OpenApi.Filters;

// The schemas of project settings. Their members describe the catalog they come from (conformance/project-settings.json)
// instead of listing its values as enums, so the SDKs carry no second copy of the colors, icons, date formats and time
// zones. The merge patch has every member optional and nullable (RFC 7396: null removes), also the members of a type,
// which ProjectTypeAppearanceMergePatch states: a reference to ProjectTypeAppearance cannot be nullable in OpenAPI 3.0.
// A type, in a write and in a patch, has no members but its four (additionalProperties false).
public class ProjectSettingsDocumentFilter : IDocumentFilter
{
    public const string AppearancePatchSchema = "ProjectTypeAppearanceMergePatch";

    private const string Catalog = "the catalog of project settings (conformance/project-settings.json)";

    private static readonly Dictionary<string, string> SettingsMembers = new(StringComparer.Ordinal)
    {
        [nameof(ProjectSettings.Types)] = "The appearance of each annotation type the project changed, keyed by its lower-case type code (rst, unt, sbt, usg, cnt, exe, uxe; any case in a write). A type left out has its built-in appearance.",
        [nameof(ProjectSettings.DefaultTimeZone)] = "The IANA time zone id new annotations start in, one of the timeZones of " + Catalog + " in any case, stored in its canonical spelling; absent for UTC. A default of the clients: the API never applies it.",
        [nameof(ProjectSettings.DateFormat)] = "How clients show dates: yyyy-mm-dd, dd.mm.yyyy, dd/mm/yyyy, mm/dd/yyyy or d mmm yyyy, the dateFormats of " + Catalog + ", in any case; absent for yyyy-mm-dd.",
        [nameof(ProjectSettings.Version)] = "Raised by every change, 0 before the first; the ETag is this number in quotes. Ignored in a write.",
        [nameof(ProjectSettings.Modified)] = "Who made the last change, and when; absent before the first. Ignored in a write.",
    };

    private static readonly Dictionary<string, string> AppearanceMembers = new(StringComparer.Ordinal)
    {
        [nameof(ProjectTypeAppearance.Label)] = "The singular name of the type, such as Company: trimmed, at most 40 characters; absent for the built-in name.",
        [nameof(ProjectTypeAppearance.Plural)] = "The plural, only with a Label: trimmed, at most 40 characters; clients derive it from the label when it is absent.",
        [nameof(ProjectTypeAppearance.Color)] = "One of the colors of " + Catalog + ", such as pink, in any case; absent for the built-in color.",
        [nameof(ProjectTypeAppearance.Icon)] = "One of the icons of " + Catalog + ", such as i-lucide-building-2, in any case; absent for the built-in icon.",
    };

    public void Apply(IHttpRequestDataObject req, OpenApiDocument document)
    {
        var schemas = document.Components?.Schemas;

        if (schemas is null)
        {
            return;
        }

        if (schemas.TryGetValue(nameof(ProjectSettings), out var settings))
        {
            Describe(settings, SettingsMembers);
        }

        if (schemas.TryGetValue(nameof(ProjectTypeAppearance), out var appearance))
        {
            Describe(appearance, AppearanceMembers);

            // the API refuses any other member of a type (unknown-member); Types itself stays a map of the type codes
            appearance.AdditionalPropertiesAllowed = false;
        }

        if (!schemas.TryGetValue(nameof(ProjectSettingsMergePatch), out var patch) || patch.Properties is null)
        {
            return;
        }

        patch.Required = new HashSet<string>();
        Describe(patch, SettingsMembers);

        foreach (var property in patch.Properties.Values)
        {
            property.Nullable = true;
        }

        // A schema of its own, nullable itself, rather than an inline one, which NSwag would name Anonymous.
        schemas[AppearancePatchSchema] = new OpenApiSchema
        {
            Type = "object",
            Nullable = true,
            AdditionalPropertiesAllowed = false,
            Description = "A merge patch of the appearance of one annotation type: a member left out keeps its value, null goes back to its built-in value; a null type goes back to its built-in appearance.",
            Properties = AppearanceMembers.ToDictionary(
                member => member.Key,
                member => new OpenApiSchema { Type = "string", Nullable = true, Description = member.Value }),
        };

        patch.Properties[nameof(ProjectSettingsMergePatch.Types)] = new OpenApiSchema
        {
            Type = "object",
            Nullable = true,
            Description = SettingsMembers[nameof(ProjectSettings.Types)] + " A null type goes back to its built-in appearance, a null member of a type to its built-in value.",
            AdditionalProperties = new OpenApiSchema
            {
                Reference = new OpenApiReference { Type = ReferenceType.Schema, Id = AppearancePatchSchema },
            },
        };
    }

    private static void Describe(OpenApiSchema schema, Dictionary<string, string> descriptions)
    {
        if (schema.Properties is null)
        {
            return;
        }

        // a reference (Modified) keeps the description of its schema: OpenAPI 3.0 ignores the siblings of $ref
        foreach (var (name, property) in schema.Properties)
        {
            if (property.Reference is null && descriptions.TryGetValue(name, out var description))
            {
                property.Description = description;
            }
        }
    }
}
