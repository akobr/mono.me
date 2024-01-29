using _42.Platform.Storyteller.Entities;

namespace _42.Platform.Storyteller
{
    public static class AnnotationExtensions
    {
        public static FullKey GetFullKey(this Annotation @this, string organizationName)
        {
            return FullKey.Create(AnnotationKey.Parse(@this.AnnotationKey), organizationName, @this.ProjectName, @this.ViewName);
        }
    }
}
