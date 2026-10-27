using _42.Platform.Storyteller.Sdk.Configuring;
using _42.Platform.Storyteller.Sdk.Credentials;

namespace _42.Platform.Storyteller.Sdk.Hosting.Reading;

// The keys of the catalog: a full key, or an annotation key in the organization, project and view of the options' Key,
// with the type code lower-cased, as the keys of the documents have it.
internal static class CatalogKeys
{
    public static FullKey Parse(string key, FullKey? scope, string parameterName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(key, parameterName);

        var text = key.Trim();

        if (text.StartsWith(Constants.FullKeyStartChar))
        {
            return FullKey.TryParse(text, out var fullKey)
                ? Normalize(fullKey)
                : throw new ArgumentException($"'{text}' is not a full key, @{{organization}}.{{project}}.{{view}}.{{annotation key}}.", parameterName);
        }

        if (!AnnotationKey.TryParse(text, out var annotationKey))
        {
            throw new ArgumentException(
                $"'{text}' is neither a full key, @{{organization}}.{{project}}.{{view}}.{{annotation key}}, nor an annotation key.",
                parameterName);
        }

        return scope is not null
            ? Normalize(FullKey.Create(annotationKey, scope))
            : throw new ArgumentException(
                $"'{text}' is an annotation key, and there is no default scope for it: pass a full key, or set {StorytellerVariables.KEY} or {nameof(StorytellerConfigurationOptions)}.{nameof(StorytellerConfigurationOptions.Key)}.",
                parameterName);
    }

    public static FullKey Normalize(FullKey key)
    {
        ArgumentNullException.ThrowIfNull(key);

        var typeCode = key.Annotation.TypeCode;
        var lowerCode = typeCode.ToLowerInvariant();

        if (string.Equals(typeCode, lowerCode, StringComparison.Ordinal))
        {
            return key;
        }

        var annotation = key.Annotation.ToString();
        return FullKey.Create(AnnotationKey.Parse(lowerCode + annotation[typeCode.Length..]), key);
    }
}
