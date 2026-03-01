using Microsoft.Azure.Cosmos;

namespace _42.Platform.Storyteller;

public static class FullKeyExtensions
{
    public static string GetPartitionKey(this FullKey @this)
    {
        return @this.GetPartitionKey(@this.Annotation);
    }

    public static string GetPartitionKey(this FullKey @this, AnnotationKey annotationKey)
    {
        return annotationKey.Type is AnnotationType.Subject or AnnotationType.Context
            ? $"{@this.ProjectName}.{AnnotationTypeCodes.Subject}.{annotationKey.SubjectName}"
            : $"{@this.ProjectName}.{AnnotationTypeCodes.Responsibility}.{annotationKey.ResponsibilityName}";
    }

    public static PartitionKey GetCosmosPartitionKey(this FullKey @this)
    {
        return new PartitionKey(@this.GetPartitionKey());
    }

    public static string GetCosmosItemId(this FullKey @this)
    {
        return @this.GetCosmosItemId(@this.Annotation);
    }

    public static string GetCosmosItemId(this FullKey @this, AnnotationKey annotationKey)
    {
        return $"{@this.ViewName}.{annotationKey}";
    }
}
