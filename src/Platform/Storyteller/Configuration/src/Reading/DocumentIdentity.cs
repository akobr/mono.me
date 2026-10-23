using System.Text.Json;
using _42.Platform.Storyteller.Sdk.Configuring.Settings;

namespace _42.Platform.Storyteller.Sdk.Configuring.Reading;

// What a document is: the endpoint, the scope, the annotation key with its type code lower-cased, and the mode. A tag
// is only ever sent for the identity it came with.
internal sealed record DocumentIdentity
{
    private DocumentIdentity(string endpoint, string organization, string project, string view, string annotationKey, StorytellerReadMode mode)
    {
        Endpoint = endpoint;
        Organization = organization;
        Project = project;
        View = view;
        AnnotationKey = annotationKey;
        Mode = mode;
        Key = $"{Constants.FullKeyStartChar}{organization}.{project}.{view}.{annotationKey}";
    }

    // Scheme, host, port and path, without a trailing slash.
    public string Endpoint { get; }

    public string Organization { get; }

    public string Project { get; }

    public string View { get; }

    public string AnnotationKey { get; }

    public StorytellerReadMode Mode { get; }

    // @organization.project.view.annotationKey, as the identity has it.
    public string Key { get; }

    public static DocumentIdentity Create(Uri endpoint, FullKey key, StorytellerReadMode mode)
    {
        ArgumentNullException.ThrowIfNull(endpoint);
        ArgumentNullException.ThrowIfNull(key);

        var annotation = key.Annotation.ToString();
        var typeCode = key.Annotation.TypeCode;

        return new DocumentIdentity(
            LocalUrl.Describe(endpoint).TrimEnd('/'),
            key.OrganizationName,
            key.ProjectName,
            key.ViewName,
            typeCode.ToLowerInvariant() + annotation[typeCode.Length..],
            mode);
    }

    // Unambiguous text of the identity, for hashing and comparison: a JSON array, so no value can fake a separator.
    public string ToCanonicalString()
    {
        return JsonSerializer.Serialize(new[] { Endpoint, Organization, Project, View, AnnotationKey, ToText(Mode) });
    }

    public override string ToString()
    {
        return $"{Key} ({ToText(Mode)}) at {Endpoint}";
    }

    internal static string ToText(StorytellerReadMode mode)
    {
        return mode == StorytellerReadMode.Effective ? "effective" : "resolved";
    }
}
