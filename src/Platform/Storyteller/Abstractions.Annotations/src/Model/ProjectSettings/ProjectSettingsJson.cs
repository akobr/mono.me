using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;

namespace _42.Platform.Storyteller;

// Project settings as JSON: Read takes a settings document of any client (an API body, a data set's file, a file of
// sform), ToJson writes the canonical PascalCase shapes, ApplyMergePatch applies an RFC 7396 merge patch, and
// ReadMergePatch checks a merge patch of settings and reads the result. Member names and type codes match in any ASCII
// case everywhere.
public static class ProjectSettingsJson
{
    private const string FormatMember = "Format";
    private const string TypesMember = nameof(ProjectSettings.Types);
    private const string DefaultTimeZoneMember = nameof(ProjectSettings.DefaultTimeZone);
    private const string DateFormatMember = nameof(ProjectSettings.DateFormat);
    private const string VersionMember = nameof(ProjectSettings.Version);
    private const string ModifiedMember = nameof(ProjectSettings.Modified);

    private const string DocumentMembersText = "Types, DefaultTimeZone and DateFormat (and the read-only Format, Version and Modified)";
    private const string AppearanceMembersText = "Label, Plural, Color and Icon";

    private static readonly string[] DocumentMembers =
        [FormatMember, TypesMember, DefaultTimeZoneMember, DateFormatMember, VersionMember, ModifiedMember];

    private static readonly string[] AppearanceMembers =
    [
        nameof(ProjectTypeAppearance.Label),
        nameof(ProjectTypeAppearance.Plural),
        nameof(ProjectTypeAppearance.Color),
        nameof(ProjectTypeAppearance.Icon),
    ];

    // As the API writes an AuditStamp: PascalCase, enums by name, no nulls.
    private static readonly JsonSerializerOptions StampOptions = new()
    {
        Converters = { new JsonStringEnumConverter() },
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    public static ProjectSettingsCheck Read(JsonNode? node)
    {
        return Read(node, ProjectSettingsCatalog.Default);
    }

    // A settings document: an object with Types, DefaultTimeZone and DateFormat, null meaning none; Format is optional
    // and must be the marker; Version and Modified are read-only and ignored; any other member is a problem. The values
    // then go through ProjectSettingsRules.Normalize, so the result is normalized (Version 0, no Modified) or refused
    // with every problem.
    public static ProjectSettingsCheck Read(JsonNode? node, ProjectSettingsCatalog catalog)
    {
        ArgumentNullException.ThrowIfNull(catalog);

        var problems = new List<ProjectSettingsProblem>();

        if (node is not JsonObject document)
        {
            problems.Add(new ProjectSettingsProblem(ProjectSettingsText.Root, ProjectSettingsRules.WrongType, "The project settings must be a JSON object."));
            return new ProjectSettingsCheck(null, problems);
        }

        var members = ReadMembers(document, ProjectSettingsText.Root, DocumentMembers, problems, DocumentMembersText);

        if (ReadText(members, FormatMember, ProjectSettingsText.Root, problems) is { } format && format != ProjectSettings.FileFormat)
        {
            problems.Add(new ProjectSettingsProblem(
                ProjectSettingsText.Property(ProjectSettingsText.Root, FormatMember),
                ProjectSettingsRules.Format,
                $"The format '{format}' is not {ProjectSettings.FileFormat}."));
        }

        var settings = new ProjectSettings
        {
            Types = ReadTypes(members.GetValueOrDefault(TypesMember), catalog, problems),
            DefaultTimeZone = ReadText(members, DefaultTimeZoneMember, ProjectSettingsText.Root, problems),
            DateFormat = ReadText(members, DateFormatMember, ProjectSettingsText.Root, problems),
        };

        var check = ProjectSettingsRules.Normalize(settings, catalog);

        if (problems.Count == 0)
        {
            return check;
        }

        problems.AddRange(check.Problems);
        return new ProjectSettingsCheck(null, problems);
    }

    public static JsonObject ToJson(ProjectSettings settings, ProjectSettingsJsonShape shape)
    {
        return ToJson(settings, shape, ProjectSettingsCatalog.Default);
    }

    // Api: Types (always, {} for none), DefaultTimeZone and DateFormat when set, Version, and Modified when set. File: the
    // Format marker, Types, DefaultTimeZone and DateFormat. Type codes in the catalog's order, members of a type in the
    // order Label, Plural, Color, Icon, no nulls.
    public static JsonObject ToJson(ProjectSettings settings, ProjectSettingsJsonShape shape, ProjectSettingsCatalog catalog)
    {
        ArgumentNullException.ThrowIfNull(settings);
        ArgumentNullException.ThrowIfNull(catalog);

        var json = new JsonObject();

        if (shape == ProjectSettingsJsonShape.File)
        {
            json[FormatMember] = ProjectSettings.FileFormat;
        }

        var types = new JsonObject();
        var ordered = (settings.Types ?? new Dictionary<string, ProjectTypeAppearance>())
            .Select((entry, position) => (entry.Key, entry.Value, Order: catalog.IndexOfTypeCode(entry.Key) is var index and >= 0 ? index : int.MaxValue, Position: position))
            .OrderBy(entry => entry.Order)
            .ThenBy(entry => entry.Position);

        foreach (var (code, appearance, _, _) in ordered)
        {
            if (ToJson(appearance) is { Count: > 0 } item)
            {
                types[code] = item;
            }
        }

        json[TypesMember] = types;
        AddText(json, DefaultTimeZoneMember, settings.DefaultTimeZone);
        AddText(json, DateFormatMember, settings.DateFormat);

        if (shape == ProjectSettingsJsonShape.Api)
        {
            json[VersionMember] = settings.Version;

            if (settings.Modified is not null)
            {
                json[ModifiedMember] = JsonSerializer.SerializeToNode(settings.Modified, StampOptions);
            }
        }

        return json;
    }

    public static ProjectSettingsCheck ReadMergePatch(ProjectSettings settings, JsonObject patch)
    {
        return ReadMergePatch(settings, patch, ProjectSettingsCatalog.Default);
    }

    // The settings after an RFC 7396 merge patch, checked as Read checks a document. The names of the patch come first,
    // checked as Read checks the names of a document, at the root, under Types and in a type: a name that is no member or
    // no type code (also with null), and two names that differ only in case, are problems and are not applied, so no
    // member of a patch overrides or removes another one and nothing is dropped silently (ApplyMergePatch alone lets the
    // later of two such names win). The rest is merged into the settings in their Api shape and read; the result is the
    // normalized settings, or every problem of both steps.
    public static ProjectSettingsCheck ReadMergePatch(ProjectSettings settings, JsonObject patch, ProjectSettingsCatalog catalog)
    {
        ArgumentNullException.ThrowIfNull(settings);
        ArgumentNullException.ThrowIfNull(patch);
        ArgumentNullException.ThrowIfNull(catalog);

        var problems = new List<ProjectSettingsProblem>();
        var named = CheckPatchNames(patch, catalog, problems);
        var check = Read(ApplyMergePatch(ToJson(settings, ProjectSettingsJsonShape.Api, catalog), named), catalog);

        if (problems.Count == 0)
        {
            return check;
        }

        problems.AddRange(check.Problems);
        return new ProjectSettingsCheck(null, problems);
    }

    // RFC 7396 on a copy of the target: null removes a member, an object merges into an object, any other value replaces;
    // a member name matches the target's in any ASCII case and keeps the target's spelling. Neither input changes. The
    // names of the patch are not checked (two that differ only in case apply in their order); ReadMergePatch checks them.
    public static JsonObject ApplyMergePatch(JsonObject target, JsonObject patch)
    {
        ArgumentNullException.ThrowIfNull(target);
        ArgumentNullException.ThrowIfNull(patch);

        var result = (JsonObject)target.DeepClone();
        MergeInto(result, patch);
        return result;
    }

    private static void MergeInto(JsonObject target, JsonObject patch)
    {
        foreach (var (name, value) in patch)
        {
            var matches = target
                .Where(member => ProjectSettingsText.EqualsIgnoreCase(member.Key, name))
                .Select(member => member.Key)
                .ToList();

            if (value is null)
            {
                matches.ForEach(match => target.Remove(match));
                continue;
            }

            var key = matches.Count > 0 ? matches[0] : name;
            matches.Skip(1).ToList().ForEach(match => target.Remove(match));

            if (value is JsonObject patchObject)
            {
                if (target[key] is not JsonObject into)
                {
                    into = new JsonObject();
                    target[key] = into;
                }

                MergeInto(into, patchObject);
            }
            else
            {
                target[key] = value.DeepClone();
            }
        }
    }

    // A copy of the patch with only the members whose names Read takes, by their canonical names: the members of the
    // document, the type codes under an object Types, and the members of a type that is an object. Every other name and
    // every name that collides with another in case is a problem, as Read reports it, and is left out.
    private static JsonObject CheckPatchNames(JsonObject patch, ProjectSettingsCatalog catalog, List<ProjectSettingsProblem> problems)
    {
        var named = new JsonObject();

        foreach (var (name, value) in ReadMembers(patch, ProjectSettingsText.Root, DocumentMembers, problems, DocumentMembersText))
        {
            named[name] = name == TypesMember && value is JsonObject types
                ? CheckPatchTypeNames(types, catalog, problems)
                : value?.DeepClone();
        }

        return named;
    }

    private static JsonObject CheckPatchTypeNames(JsonObject types, ProjectSettingsCatalog catalog, List<ProjectSettingsProblem> problems)
    {
        var named = new JsonObject();

        foreach (var (code, value) in ReadTypeCodes(types, catalog, problems))
        {
            if (value is not JsonObject appearance)
            {
                named[code] = value?.DeepClone();
                continue;
            }

            var item = new JsonObject();
            var path = ProjectSettingsText.Property(ProjectSettingsRules.TypesPath, code);

            foreach (var (member, memberValue) in ReadMembers(appearance, path, AppearanceMembers, problems, AppearanceMembersText))
            {
                item[member] = memberValue?.DeepClone();
            }

            named[code] = item;
        }

        return named;
    }

    // The known members by their canonical name; an unknown member and two members that differ only in case are problems
    // (and the colliding members are not read).
    private static Dictionary<string, JsonNode?> ReadMembers(
        JsonObject node,
        string path,
        IReadOnlyList<string> known,
        List<ProjectSettingsProblem> problems,
        string membersText)
    {
        var members = new Dictionary<string, JsonNode?>(StringComparer.Ordinal);
        var collisions = new HashSet<string>(StringComparer.Ordinal);

        foreach (var (name, value) in node)
        {
            var canonical = known.FirstOrDefault(member => ProjectSettingsText.EqualsIgnoreCase(member, name));

            if (canonical is null)
            {
                problems.Add(new ProjectSettingsProblem(
                    ProjectSettingsText.Property(path, name),
                    ProjectSettingsRules.UnknownMember,
                    $"'{name}' is not a member; the members are {membersText}."));
            }
            else if (!members.TryAdd(canonical, value))
            {
                collisions.Add(canonical);
            }
        }

        foreach (var canonical in known.Where(collisions.Contains))
        {
            members.Remove(canonical);
            problems.Add(new ProjectSettingsProblem(
                ProjectSettingsText.Property(path, canonical),
                ProjectSettingsRules.CaseCollision,
                $"The member {canonical} is given more than once, in different cases."));
        }

        return members;
    }

    // Keyed by the type code in the catalog's spelling, so the rules see no case problems of their own; an unknown code
    // and two codes that differ only in case are problems here.
    private static Dictionary<string, ProjectTypeAppearance> ReadTypes(JsonNode? node, ProjectSettingsCatalog catalog, List<ProjectSettingsProblem> problems)
    {
        var types = new Dictionary<string, ProjectTypeAppearance>(StringComparer.Ordinal);

        if (node is null)
        {
            return types;
        }

        if (node is not JsonObject typesObject)
        {
            problems.Add(new ProjectSettingsProblem(ProjectSettingsRules.TypesPath, ProjectSettingsRules.WrongType, "Types must be an object of type codes, or null."));
            return types;
        }

        foreach (var (code, value) in ReadTypeCodes(typesObject, catalog, problems))
        {
            var path = ProjectSettingsText.Property(ProjectSettingsRules.TypesPath, code);

            if (value is null)
            {
                continue;
            }

            if (value is not JsonObject appearance)
            {
                problems.Add(new ProjectSettingsProblem(path, ProjectSettingsRules.WrongType, $"The type {code} must be an object of Label, Plural, Color and Icon, or null."));
                continue;
            }

            var members = ReadMembers(appearance, path, AppearanceMembers, problems, AppearanceMembersText);

            types[code] = new ProjectTypeAppearance
            {
                Label = ReadText(members, nameof(ProjectTypeAppearance.Label), path, problems),
                Plural = ReadText(members, nameof(ProjectTypeAppearance.Plural), path, problems),
                Color = ReadText(members, nameof(ProjectTypeAppearance.Color), path, problems),
                Icon = ReadText(members, nameof(ProjectTypeAppearance.Icon), path, problems),
            };
        }

        return types;
    }

    // The keys of Types by the type code in the catalog's spelling; a key that is no type code and two keys that differ
    // only in case are problems (and the colliding keys are not read).
    private static Dictionary<string, JsonNode?> ReadTypeCodes(JsonObject types, ProjectSettingsCatalog catalog, List<ProjectSettingsProblem> problems)
    {
        var byCode = new Dictionary<string, JsonNode?>(StringComparer.Ordinal);
        var collisions = new List<string>();

        foreach (var (key, value) in types)
        {
            if (!catalog.TryGetTypeCode(key, out var code))
            {
                problems.Add(new ProjectSettingsProblem(
                    ProjectSettingsText.Property(ProjectSettingsRules.TypesPath, key),
                    ProjectSettingsRules.TypeCode,
                    $"'{key}' is not a type code; the codes are {string.Join(", ", catalog.TypeCodes)}."));
            }
            else if (!byCode.TryAdd(code, value) && !collisions.Contains(code))
            {
                collisions.Add(code);
            }
        }

        foreach (var code in collisions)
        {
            byCode.Remove(code);
            problems.Add(new ProjectSettingsProblem(
                ProjectSettingsText.Property(ProjectSettingsRules.TypesPath, code),
                ProjectSettingsRules.TypeCode,
                $"The type code {code} is given more than once, in different cases."));
        }

        return byCode;
    }

    // A string, or null for null and a missing member; any other value is a problem (and then read as none).
    private static string? ReadText(Dictionary<string, JsonNode?> members, string name, string parent, List<ProjectSettingsProblem> problems)
    {
        if (!members.TryGetValue(name, out var value) || value is null)
        {
            return null;
        }

        if (value is JsonValue scalar && scalar.GetValueKind() == JsonValueKind.String)
        {
            return scalar.TryGetValue<string>(out var text) ? text : scalar.ToString();
        }

        problems.Add(new ProjectSettingsProblem(
            ProjectSettingsText.Property(parent, name),
            ProjectSettingsRules.WrongType,
            $"{name} must be a string or null, not {Describe(value.GetValueKind())}."));
        return null;
    }

    private static string Describe(JsonValueKind kind)
    {
        return kind switch
        {
            JsonValueKind.Object => "an object",
            JsonValueKind.Array => "an array",
            JsonValueKind.Number => "a number",
            JsonValueKind.True or JsonValueKind.False => "a boolean",
            _ => "this value",
        };
    }

    private static JsonObject ToJson(ProjectTypeAppearance? appearance)
    {
        var item = new JsonObject();

        if (appearance is not null)
        {
            AddText(item, nameof(ProjectTypeAppearance.Label), appearance.Label);
            AddText(item, nameof(ProjectTypeAppearance.Plural), appearance.Plural);
            AddText(item, nameof(ProjectTypeAppearance.Color), appearance.Color);
            AddText(item, nameof(ProjectTypeAppearance.Icon), appearance.Icon);
        }

        return item;
    }

    private static void AddText(JsonObject json, string name, string? value)
    {
        if (value is not null)
        {
            json[name] = value;
        }
    }
}
