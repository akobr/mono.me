namespace _42.Platform.Storyteller.Annotating;

public enum AnnotationDeletedFilter
{
    // Active annotations only, the default of every existing read.
    Exclude = 0,

    // Soft-deleted annotations only.
    Only,

    // Both.
    Include,
}
