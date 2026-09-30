namespace _42.Platform.Storyteller.Configuring;

public class TemplateConcurrencyException(string project, string view, string annotationType, int retries)
    : Exception($"Failed to update template '{annotationType}' in project '{project}' and view '{view}' after multiple retries ({retries}) due to concurrent modifications.")
{
    public string Project { get; } = project;

    public string View { get; } = view;

    public string AnnotationType { get; } = annotationType;
}
