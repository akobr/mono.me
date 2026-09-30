namespace _42.Platform.Storyteller.Configuring;

public class TemplateNotFoundException(string project, string view, string annotationType)
    : Exception($"Template for '{annotationType}' does not exist in project '{project}' and view '{view}'.")
{
    public string Project { get; } = project;

    public string View { get; } = view;

    public string AnnotationType { get; } = annotationType;
}
