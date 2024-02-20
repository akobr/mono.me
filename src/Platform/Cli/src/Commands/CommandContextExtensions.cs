using _42.Platform.Storyteller;

namespace _42.Platform.Cli.Commands;

public static class CommandContextExtensions
{
    public static FullKey GetFullKey(this ICommandContext @this, AnnotationKey annotationKey)
    {
        return FullKey.Create(
            annotationKey,
            @this.OrganizationName,
            @this.ProjectName,
            @this.ViewName);
    }
}
