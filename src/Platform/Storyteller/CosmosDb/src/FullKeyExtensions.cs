using Microsoft.Azure.Cosmos;

namespace _42.Platform.Storyteller;

public static class FullKeyExtensions
{
    public static string GetResponsibilityPartitionKey(string projectName, string responsibilityName)
    {
        return $"{projectName}.{responsibilityName}";
    }

    public static string GetSubjectPartitionKey(string projectName, string subjectName)
    {
        return $"{projectName}.{subjectName}";
    }

    public static PartitionKey GetCosmosResponsibilityPartitionKey(string projectName, string responsibilityName)
    {
        return new PartitionKey(GetResponsibilityPartitionKey(projectName, responsibilityName));
    }

    public static PartitionKey GetCosmosSubjectPartitionKey(string projectName, string subjectName)
    {
        return new PartitionKey(GetSubjectPartitionKey(projectName, subjectName));
    }

    public static string GetPartitionKey(this FullKey @this)
    {
        return @this.Annotation.Type >= AnnotationType.Subject
            ? $"{@this.OrganizationName}.{@this.ProjectName}.{@this.Annotation.SubjectName}"
            : $"{@this.OrganizationName}.{@this.ProjectName}";
    }

    public static PartitionKey GetCosmosPartitionKey(this FullKey @this)
    {
        return new PartitionKey(GetPartitionKey(@this));
    }

    public static string GetCosmosItemKey(this FullKey @this)
    {
        return $"{@this.ViewName}_{@this.Annotation}";
    }
}
