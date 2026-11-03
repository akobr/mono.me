using _42.Platform.Storyteller.Sdk.Configuring.Reading;

namespace _42.Platform.Storyteller.Sdk.Configuring.Engine;

// The key of a switch: a full key in the configured organization and project, or an annotation key in the active
// organization, project and view, which only the run knows.
internal sealed class SwitchTarget
{
    private readonly FullKey? _fullKey;
    private readonly AnnotationKey? _annotationKey;

    private SwitchTarget(FullKey? fullKey, AnnotationKey? annotationKey)
    {
        _fullKey = fullKey;
        _annotationKey = annotationKey;
    }

    // A machine credential belongs to one project, so a switch never leaves the configured organization and project.
    public static SwitchTarget Parse(string key, DocumentIdentity configured)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(key);

        var text = key.Trim();

        if (!text.StartsWith(Constants.FullKeyStartChar))
        {
            return AnnotationKey.TryParse(text, out var annotationKey)
                ? new SwitchTarget(null, annotationKey)
                : throw new ArgumentException(
                    $"'{text}' is neither a full key, @{{organization}}.{{project}}.{{view}}.{{annotation key}}, nor an annotation key.",
                    nameof(key));
        }

        if (!FullKey.TryParse(text, out var fullKey))
        {
            throw new ArgumentException($"'{text}' is not a full key, @{{organization}}.{{project}}.{{view}}.{{annotation key}}.", nameof(key));
        }

        if (!string.Equals(fullKey.OrganizationName, configured.Organization, StringComparison.Ordinal)
            || !string.Equals(fullKey.ProjectName, configured.Project, StringComparison.Ordinal))
        {
            throw new ArgumentException(
                $"'{text}' is outside the organization and project of the configuration, {configured.Organization}.{configured.Project}; a machine credential belongs to one project.",
                nameof(key));
        }

        return new SwitchTarget(fullKey, null);
    }

    public DocumentIdentity Resolve(DocumentIdentity active, Uri endpoint)
    {
        var fullKey = _fullKey ?? FullKey.Create(_annotationKey!, active.Organization, active.Project, active.View);
        return DocumentIdentity.Create(endpoint, fullKey, active.Mode);
    }
}
