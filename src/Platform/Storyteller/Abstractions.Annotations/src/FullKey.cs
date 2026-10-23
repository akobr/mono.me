using System;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using System.Text;

namespace _42.Platform.Storyteller;

public class FullKey
{
    private FullKey(AnnotationKey annotationKey, string organizationName, string projectName, string viewName)
    {
        Annotation = annotationKey;
        OrganizationName = organizationName;
        ProjectName = projectName;
        ViewName = viewName;
    }

    public AnnotationKey Annotation { get; }

    public string OrganizationName { get; }

    public string ProjectName { get; }

    public string ViewName { get; }

    public static implicit operator string(FullKey key)
    {
        return key.ToString();
    }

    public override string ToString()
    {
        var builder = new StringBuilder();
        builder.Append(Constants.FullKeyStartChar);
        builder.Append(string.Join(Constants.DefaultKeySeparator, OrganizationName, ProjectName, ViewName));
        builder.Append(Constants.DefaultKeySeparatorChar);
        builder.Append(Annotation);
        return builder.ToString();
    }

    public static bool TryParse(string text, [MaybeNullWhen(false)] out FullKey key)
    {
        return TryParse(text, out key, out _);
    }

    // Throws FormatException with the reason the text is not a full key.
    public static FullKey Parse(string text)
    {
        if (!TryParse(text, out var key, out var error))
        {
            throw new FormatException($"Invalid annotation full key '{text}': {error}");
        }

        return key;
    }

    public static FullKey Create(AnnotationKey annotationKey, string organizationName, string projectName, string viewName)
    {
        return new FullKey(annotationKey, organizationName, projectName, viewName);
    }

    public static FullKey Create(AnnotationKey annotationKey, FullKey parentFullKey)
    {
        return new FullKey(annotationKey, parentFullKey.OrganizationName, parentFullKey.ProjectName, parentFullKey.ViewName);
    }

    // @organization.project.view plus an annotation key of 2 to 5 segments, so 5 to 8 segments;
    // the annotation key decides its own count per type (an 8-segment key is a unit of execution).
    private static bool TryParse(string? text, [MaybeNullWhen(false)] out FullKey key, [MaybeNullWhen(true)] out string error)
    {
        key = null;

        if (string.IsNullOrWhiteSpace(text)
            || !text.StartsWith(Constants.FullKeyStartChar))
        {
            error = $"it must start with '{Constants.FullKeyStartChar}'.";
            return false;
        }

        var segments = text[1..].Split(Constants.KeySeparators);

        if (segments.Any(string.IsNullOrWhiteSpace))
        {
            error = "the organization, project, view and every segment of the annotation key must be non-empty.";
            return false;
        }

        if (segments.Length is < 5 or > 8)
        {
            error = "it must have the organization, project, view and an annotation key of 2 to 5 segments.";
            return false;
        }

        if (!AnnotationTypeCodes.ValidCodes.ContainsKey(segments[3]))
        {
            error = $"'{segments[3]}' is not an annotation type code.";
            return false;
        }

        var annotationKey = new AnnotationKey(segments[3..]);

        if (!annotationKey.IsValid())
        {
            error = $"'{annotationKey}' is not a valid annotation key.";
            return false;
        }

        key = new FullKey(annotationKey, segments[0], segments[1], segments[2]);
        error = null;
        return true;
    }
}
