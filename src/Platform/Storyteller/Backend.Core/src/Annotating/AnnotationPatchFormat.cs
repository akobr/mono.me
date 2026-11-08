namespace _42.Platform.Storyteller.Annotating;

public enum AnnotationPatchFormat
{
    // RFC 7396, application/merge-patch+json.
    MergePatch = 0,

    // RFC 6902, application/json-patch+json.
    JsonPatch,
}
