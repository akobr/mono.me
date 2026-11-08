using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Reflection;
using System.Text.Json;
using System.Text.RegularExpressions;
using _42.Platform.Cli.Services;
using _42.Platform.Storyteller;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using CoreAnnotation = _42.Platform.Storyteller.Annotation;
using CoreUnit = _42.Platform.Storyteller.Unit;
using NewtonsoftSerializer = Newtonsoft.Json.JsonSerializer;
using Sdk = _42.Platform.Storyteller.Sdk;

namespace _42.Platform.Cli.Model;

// The document of sform story edit: the editable members of an annotation, with the names and in the order of the file
// of an annotation in an export (ViewAnnotationDocument). Unlike that file, it lists every member (null when unset, []
// and {} for no labels and no values), so the editor shows what can be filled in. The key decides the rest, and the
// document replaces the content: what it does not hold is cleared.
public static class AnnotationEditDocument
{
    // Newtonsoft's ISO format, in which a date of the document is written, so it compares as text with the edited file.
    private const string DateFormat = "yyyy'-'MM'-'dd'T'HH':'mm':'ss.FFFFFFFzzz";

    private const string Title = nameof(CoreAnnotation.Title);
    private const string Description = nameof(CoreAnnotation.Description);
    private const string DocumentationLink = nameof(CoreAnnotation.DocumentationLink);
    private const string Labels = nameof(CoreAnnotation.Labels);
    private const string Values = nameof(CoreAnnotation.Values);
    private const string IsDisabled = nameof(CoreAnnotation.IsDisabled);
    private const string ValidFrom = nameof(CoreAnnotation.ValidFrom);
    private const string ExpiresAt = nameof(CoreAnnotation.ExpiresAt);
    private const string TimeZone = nameof(CoreAnnotation.TimeZone);
    private const string UnitType = nameof(CoreUnit.UnitType);
    private const string UnitDefinition = nameof(CoreUnit.UnitDefinition);

    // The editable members of every annotation, in the order they are written.
    private static readonly string[] Members = [Title, Description, DocumentationLink, Labels, Values, IsDisabled, ValidFrom, ExpiresAt, TimeZone];

    // The editable members of units only.
    private static readonly string[] UnitMembers = [UnitType, UnitDefinition];

    // Per type: the editable members, and the other members of the type's model, which the key or the server decides.
    private static readonly Dictionary<AnnotationType, MemberSet> MemberSets = Enum.GetValues<AnnotationType>()
        .ToDictionary(type => type, MemberSet.Create);

    // The time and the offset that end an ISO 8601 date and time with an offset: Z, +hh:mm or +hh. A date alone has none,
    // even when it ends like one ("2026-12-01").
    private static readonly Regex ExplicitOffset = new(@"T\d{2}:\d{2}(?::\d{2}(?:\.\d+)?)?(?:Z|[+-]\d{2}(?::\d{2})?)$", RegexOptions.CultureInvariant);

    // As the API reads a body: a member such as "$ref" or "$id" of a value is a member, not metadata of Newtonsoft.
    private static readonly NewtonsoftSerializer ContentSerializer = NewtonsoftSerializer.Create(new JsonSerializerSettings
    {
        DateParseHandling = DateParseHandling.None,
        MetadataPropertyHandling = MetadataPropertyHandling.Ignore,
    });

    /// <summary>
    /// The document to edit: the editable members of the annotation, or of a new annotation of the key's type.
    /// </summary>
    /// <param name="key">The key of the annotation, which decides its type.</param>
    /// <param name="annotation">The annotation as the API answered it, or null for a new one.</param>
    public static JObject Create(AnnotationKey key, Sdk.Annotation? annotation)
    {
        var document = new JObject
        {
            [Title] = Text(annotation?.Title),
            [Description] = Text(annotation?.Description),
            [DocumentationLink] = Text(annotation?.DocumentationLink),
            [Labels] = new JArray((annotation?.Labels ?? []).Select(label => new JValue(label))),
            [Values] = ToValues(annotation?.Values),
            [IsDisabled] = annotation?.IsDisabled is { } isDisabled ? new JValue(isDisabled) : JValue.CreateNull(),
            [ValidFrom] = Date(annotation?.ValidFrom),
            [ExpiresAt] = Date(annotation?.ExpiresAt),
            [TimeZone] = Text(annotation?.TimeZone),
        };

        if (key.Type == AnnotationType.Unit)
        {
            var unit = annotation as Sdk.Unit;
            document[UnitType] = new JValue(unit?.UnitType ?? CoreUnit.DefaultUnitType);
            document[UnitDefinition] = new JValue(unit?.UnitDefinition ?? CoreUnit.DefaultUnitDefinition);
        }

        return document;
    }

    /// <summary>
    /// Checks an edited document: its member names, the types of their values, and the rules of the server
    /// (AnnotationRules: the lengths of AnnotationLimits, an absolute http or https documentation link), in its wording,
    /// so they come up while the editor can be re-opened. The server still checks everything.
    /// </summary>
    /// <returns>The first problem, or null for a valid document.</returns>
    public static string? Validate(AnnotationKey key, JObject document)
    {
        return Read(key, document).Problem;
    }

    /// <summary>
    /// The check of the editor (<see cref="IEditorService.EditJsonAsync"/>): the problem of <see cref="Validate"/>, or
    /// the document of <see cref="Normalize"/>, which the editor compares, shows and returns.
    /// </summary>
    public static JsonEditCheck Check(AnnotationKey key, JObject document)
    {
        var problem = Validate(key, document);
        return problem is null ? JsonEditCheck.Valid(Normalize(key, document)) : JsonEditCheck.Invalid(problem);
    }

    /// <summary>
    /// A valid document in the form <see cref="Create"/> writes: every member in its name and order (null when missing),
    /// labels without duplicates, [] and {} for no labels and no values, the dates as <see cref="Create"/> writes them, and
    /// for a unit the defaults of a create for a missing type or definition.
    /// </summary>
    /// <exception cref="ArgumentException">The document is not valid (<see cref="Validate"/>).</exception>
    public static JObject Normalize(AnnotationKey key, JObject document)
    {
        var (members, problem) = Read(key, document);

        if (members is null)
        {
            throw new ArgumentException(problem, nameof(document));
        }

        var normalized = new JObject();

        foreach (var name in MemberSets[key.Type].Ordered)
        {
            var value = members.GetValueOrDefault(name);
            normalized[name] = value is null || value.Type == JTokenType.Null
                ? NormalizeMissing(name)
                : NormalizeValue(name, value);
        }

        return normalized;
    }

    /// <summary>
    /// The annotation of a normalized document, of the runtime type of the key, with the key's identity.
    /// <see cref="AnnotationExtensions.ToSdkAnnotation"/> adds the members the key decides.
    /// </summary>
    public static CoreAnnotation ToAnnotation(AnnotationKey key, FullKey fullKey, JObject normalized)
    {
        var content = new JObject();

        foreach (var property in normalized.Properties().Where(property => property.Value.Type != JTokenType.Null))
        {
            content.Add(property.Name, property.Value.DeepClone());
        }

        // Date-like strings of the values stay strings, the typed dates parse, and "$ref" or "$id" stay members.
        var annotation = (CoreAnnotation)content.ToObject(AnnotationTypes.GetRuntimeType(key.Type), ContentSerializer)!;

        return annotation with
        {
            AnnotationKey = key.ToString(),
            AnnotationType = key.Type,
            Name = key.Name,
            ProjectName = fullKey.ProjectName,
            ViewName = fullKey.ViewName,
        };
    }

    // The members of a valid document by their names, or the first problem.
    private static (Dictionary<string, JToken>? Members, string? Problem) Read(AnnotationKey key, JObject document)
    {
        var set = MemberSets[key.Type];
        var members = new Dictionary<string, JToken>(StringComparer.Ordinal);

        foreach (var property in document.Properties())
        {
            if (set.Editable.TryGetValue(property.Name, out var name))
            {
                if (members.ContainsKey(name))
                {
                    return (null, $"The member '{name}' is in the document twice.");
                }

                var problem = CheckValue(name, property.Value);

                if (problem is not null)
                {
                    return (null, problem);
                }

                members.Add(name, property.Value);
            }
            else if (UnitMembers.FirstOrDefault(member => member.Equals(property.Name, StringComparison.OrdinalIgnoreCase)) is { } unitMember)
            {
                return (null, $"'{unitMember}' is a member of units only.");
            }
            else if (set.Fixed.Contains(property.Name))
            {
                return (null, $"'{property.Name}' cannot be edited: the key decides the type, the names and the relations, and the server keeps the stamps.");
            }
            else
            {
                return (null, $"Unknown member '{property.Name}'. The members are {string.Join(", ", set.Ordered)}.");
            }
        }

        var broken = CheckRules(members);
        return broken is null ? (members, null) : (null, broken);
    }

    // The rules of AnnotationRules.GetProblems, in its wording: the first one the document breaks.
    private static string? CheckRules(Dictionary<string, JToken> members)
    {
        var title = TextOf(members, Title);
        var description = TextOf(members, Description);
        var link = TextOf(members, DocumentationLink);

        if (title?.Length > AnnotationLimits.TitleMaxLength)
        {
            return $"Title has {title.Length} characters, at most {AnnotationLimits.TitleMaxLength} are allowed.";
        }

        if (description?.Length > AnnotationLimits.DescriptionMaxLength)
        {
            return $"Description has {description.Length} characters, at most {AnnotationLimits.DescriptionMaxLength} are allowed.";
        }

        if (string.IsNullOrEmpty(link))
        {
            return null;
        }

        if (link.Length > AnnotationLimits.DocumentationLinkMaxLength)
        {
            return $"DocumentationLink has {link.Length} characters, at most {AnnotationLimits.DocumentationLinkMaxLength} are allowed.";
        }

        return IsHttpUrl(link) ? null : "DocumentationLink must be an absolute http or https URL.";
    }

    private static string? TextOf(Dictionary<string, JToken> members, string name)
    {
        return members.TryGetValue(name, out var value) && value.Type == JTokenType.String ? value.Value<string>() : null;
    }

    // As AnnotationRules.IsHttpUrl.
    private static bool IsHttpUrl(string value)
    {
        return Uri.TryCreate(value, UriKind.Absolute, out var uri)
               && (uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps)
               && !value.Any(char.IsWhiteSpace);
    }

    private static string? CheckValue(string name, JToken value)
    {
        if (value.Type == JTokenType.Null)
        {
            return null;
        }

        return name switch
        {
            IsDisabled => value.Type == JTokenType.Boolean
                ? null
                : $"'{name}' must be true, false or null.",
            ValidFrom or ExpiresAt => TryReadDate(value, out _)
                ? null
                : $"'{name}' must be a date and time with an offset, for example \"2026-12-01T00:00:00+01:00\", or null.",
            Labels => value is JArray labels && labels.All(label => label.Type == JTokenType.String)
                ? null
                : $"'{name}' must be an array of strings, or null.",
            Values => value.Type == JTokenType.Object
                ? null
                : $"'{name}' must be an object, or null.",
            _ => value.Type == JTokenType.String
                ? null
                : $"'{name}' must be a string or null.",
        };
    }

    // What the API's reader (System.Text.Json) takes as a DateTimeOffset (ISO 8601), with an explicit offset or Z: without
    // one, it would be taken in the offset of this machine, while the annotation's TimeZone is meant for that.
    private static bool TryReadDate(JToken value, out DateTimeOffset date)
    {
        switch (value.Type)
        {
            case JTokenType.Date:
                // a document parsed with dates (not the editor's file): a DateTime without an offset is local
                date = ((JValue)value).Value is DateTimeOffset offset ? offset : new DateTimeOffset(value.Value<DateTime>());
                return true;

            case JTokenType.String:
                var text = value.Value<string>()!;

                using (var json = JsonDocument.Parse(System.Text.Json.JsonSerializer.Serialize(text)))
                {
                    return json.RootElement.TryGetDateTimeOffset(out date) && ExplicitOffset.IsMatch(text);
                }

            default:
                date = default;
                return false;
        }
    }

    private static JToken NormalizeMissing(string name)
    {
        return name switch
        {
            Labels => new JArray(),
            Values => new JObject(),
            UnitType => new JValue(CoreUnit.DefaultUnitType),
            UnitDefinition => new JValue(CoreUnit.DefaultUnitDefinition),
            _ => JValue.CreateNull(),
        };
    }

    private static JToken NormalizeValue(string name, JToken value)
    {
        switch (name)
        {
            case Labels:
                // the first occurrence of each label, as ToSdkAnnotation keeps them
                return new JArray(value.Values<string>().Distinct(StringComparer.Ordinal).Select(label => new JValue(label)));

            case ValidFrom or ExpiresAt:
                TryReadDate(value, out var date);
                return Date(date);

            default:
                return value.DeepClone();
        }
    }

    // null as a JSON null: a JValue of a null string is of type String, and would not equal the parsed file.
    private static JValue Text(string? value)
    {
        return value is null ? JValue.CreateNull() : new JValue(value);
    }

    private static JValue Date(DateTimeOffset? value)
    {
        return value is { } date
            ? new JValue(date.ToString(DateFormat, CultureInfo.InvariantCulture))
            : JValue.CreateNull();
    }

    // The values as the SDK read them: date-like strings unparsed, objects and arrays as JTokens.
    private static JObject ToValues(IDictionary<string, object>? values)
    {
        var document = new JObject();

        foreach (var (name, value) in values ?? new Dictionary<string, object>())
        {
            document[name] = value switch
            {
                null => JValue.CreateNull(),
                JToken token => token.DeepClone(),
                _ => JToken.FromObject(value),
            };
        }

        return document;
    }

    private sealed record class MemberSet(IReadOnlyList<string> Ordered, IReadOnlyDictionary<string, string> Editable, IReadOnlySet<string> Fixed)
    {
        public static MemberSet Create(AnnotationType type)
        {
            var ordered = type == AnnotationType.Unit ? Members.Concat(UnitMembers).ToList() : Members.ToList();
            var editable = ordered.ToDictionary(name => name, name => name, StringComparer.OrdinalIgnoreCase);
            var fixedMembers = AnnotationTypes.GetRuntimeType(type)
                .GetProperties(BindingFlags.Public | BindingFlags.Instance)
                .Select(property => property.Name)
                .Where(name => !editable.ContainsKey(name))
                .ToHashSet(StringComparer.OrdinalIgnoreCase);

            return new MemberSet(ordered, editable, fixedMembers);
        }
    }
}
