using Microsoft.Azure.Cosmos;

namespace _42.Platform.Storyteller;

public static class FullKeyExtensions
{
    public static string GetPartitionKey(this FullKey @this)
    {
        return @this.Annotation.Type is AnnotationType.Subject or AnnotationType.Context
            ? $"{@this.ProjectName}.{@this.Annotation.SubjectName}"
            : $"{@this.ProjectName}.{@this.Annotation.ResponsibilityName}";
    }

    public static PartitionKey GetCosmosPartitionKey(this FullKey @this)
    {
        return new PartitionKey(GetPartitionKey(@this));
    }

    public static string GetCosmosItemKey(this FullKey @this)
    {
        return $"{@this.ViewName}.{@this.Annotation}";
    }
}
