using System;
using System.Diagnostics.CodeAnalysis;
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
        if (string.IsNullOrWhiteSpace(text)
            || !text.StartsWith(Constants.FullKeyStartChar))
        {
            key = null;
            return false;
        }

        var segments = text.Split(Constants.KeySeparators, StringSplitOptions.RemoveEmptyEntries);

        if (segments.Length is < 5 or > 7
            || !AnnotationTypeCodes.ValidCodes.ContainsKey(segments[3]))
        {
            key = null;
            return false;
        }

        var annotationKey = new AnnotationKey(segments[3..]);

        if (!annotationKey.IsValid())
        {
            key = null;
            return false;
        }

        key = new FullKey(annotationKey, segments[0][1..], segments[1], segments[2]);
        return true;
    }

    public static FullKey Parse(string text)
    {
        if (string.IsNullOrWhiteSpace(text)
            || !text.StartsWith(Constants.FullKeyStartChar))
        {
            throw new ArgumentNullException(nameof(text));
        }

        var segments = text.Split(Constants.KeySeparators, StringSplitOptions.RemoveEmptyEntries);

        if (segments.Length is < 5 or > 7)
        {
            throw new ArgumentOutOfRangeException(nameof(text), "Invalid annotation full key.");
        }

        var annotationKey = new AnnotationKey(segments[3..]);

        if (!annotationKey.IsValid())
        {
            throw new ArgumentOutOfRangeException(nameof(text), "Invalid annotation full key.");
        }

        return new FullKey(annotationKey, segments[0][1..], segments[1], segments[2]);
    }

    public static FullKey Create(AnnotationKey annotationKey, string organizationName, string projectName, string viewName)
    {
        return new FullKey(annotationKey, organizationName, projectName, viewName);
    }
}
