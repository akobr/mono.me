using System;
using System.Collections.Generic;
using System.Linq;

namespace _42.Platform.Storyteller.Entities.Access;

// The settings of a project as AccessPointEntity.Settings stores them. Point reads deserialize it with System.Text.Json,
// writes, queries and DbCreator's merge with the Cosmos serializer (Newtonsoft), so no member is required, none is an
// enum of its own and Types has no default entries: an old document has no settings at all, and a missing member reads
// as null. Once written, Settings is never set back to null (a reset stores empty settings with a higher Version).
public record class ProjectSettingsEntity
{
    // Keyed by lower-case type code, in the catalog's order; only the types the project changed.
    public Dictionary<string, ProjectTypeAppearanceEntity>? Types { get; init; }

    public string? DefaultTimeZone { get; init; }

    public string? DateFormat { get; init; }

    public long Version { get; init; }

    public AuditStamp? Modified { get; init; }

    public static ProjectSettingsEntity From(ProjectSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);

        var types = new Dictionary<string, ProjectTypeAppearanceEntity>(StringComparer.Ordinal);

        foreach (var (code, appearance) in Ordered(settings.Types, entry => entry.Key))
        {
            types[code] = ProjectTypeAppearanceEntity.From(appearance);
        }

        return new ProjectSettingsEntity
        {
            Types = types,
            DefaultTimeZone = settings.DefaultTimeZone,
            DateFormat = settings.DateFormat,
            Version = settings.Version,
            Modified = settings.Modified,
        };
    }

    public ProjectSettings ToModel()
    {
        var types = new Dictionary<string, ProjectTypeAppearance>(StringComparer.Ordinal);

        foreach (var (code, appearance) in Ordered(Types ?? [], entry => entry.Key))
        {
            if (appearance is not null)
            {
                types[code] = appearance.ToModel();
            }
        }

        return new ProjectSettings
        {
            Types = types,
            DefaultTimeZone = DefaultTimeZone,
            DateFormat = DateFormat,
            Version = Version,
            Modified = Modified,
        };
    }

    // The type codes in the catalog's order; a code the catalog does not know (a document changed by hand) last.
    private static IEnumerable<T> Ordered<T>(IEnumerable<T> entries, Func<T, string> code)
    {
        var catalog = ProjectSettingsCatalog.Default;
        return entries
            .Select((entry, position) => (Entry: entry, Order: catalog.IndexOfTypeCode(code(entry)), Position: position))
            .OrderBy(item => item.Order < 0 ? int.MaxValue : item.Order)
            .ThenBy(item => item.Position)
            .Select(item => item.Entry);
    }
}
