namespace _42.Platform.Storyteller.Configuring;

public class TemplateNotFoundException(string project, string annotationType)
    : Exception($"Template for '{annotationType}' does not exist in project '{project}'.")
{
    public string Project { get; } = project;

    public string AnnotationType { get; } = annotationType;
}
