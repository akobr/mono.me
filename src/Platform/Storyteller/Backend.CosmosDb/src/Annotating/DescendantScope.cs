using System.Collections.Generic;

namespace _42.Platform.Storyteller.Annotating;

// Where the descendants of a key are stored: one partition, or every responsibility partition of the project,
// and the key prefixes that cover them. The prefixes may cover more than the descendants; the loader keeps only
// keys that have the root among their ancestors.
internal sealed record DescendantScope(string? PartitionKey, string PartitionPrefix, IReadOnlyList<string> KeyPrefixes)
{
    public static IReadOnlyList<DescendantScope> Get(FullKey root)
    {
        var key = root.Annotation;
        var project = root.ProjectName;
        var responsibilities = $"{project}.{AnnotationTypeCodes.Responsibility}.";

        return key.Type switch
        {
            AnnotationType.Responsibility =>
            [
                InPartition(PartitionKeys.GetResponsibility(project, key.ResponsibilityName), $"{AnnotationTypeCodes.Unit}.", $"{AnnotationTypeCodes.Usage}.", $"{AnnotationTypeCodes.Execution}.", $"{AnnotationTypeCodes.UnitOfExecution}."),
            ],
            AnnotationType.Unit =>
            [
                InPartition(PartitionKeys.GetResponsibility(project, key.ResponsibilityName), $"{AnnotationTypeCodes.UnitOfExecution}."),
            ],
            AnnotationType.Subject =>
            [
                InPartition(PartitionKeys.GetSubject(project, key.SubjectName), $"{AnnotationTypeCodes.Context}.{key.SubjectName}."),
                AcrossPartitions(responsibilities, $"{AnnotationTypeCodes.Usage}.{key.SubjectName}.", $"{AnnotationTypeCodes.Execution}.{key.SubjectName}.", $"{AnnotationTypeCodes.UnitOfExecution}.{key.SubjectName}."),
            ],
            AnnotationType.Usage =>
            [
                InPartition(PartitionKeys.GetResponsibility(project, key.ResponsibilityName), $"{AnnotationTypeCodes.Execution}.{key.SubjectName}.{key.ResponsibilityName}.", $"{AnnotationTypeCodes.UnitOfExecution}.{key.SubjectName}.{key.ResponsibilityName}."),
            ],
            AnnotationType.Context =>
            [
                AcrossPartitions(responsibilities, $"{AnnotationTypeCodes.Execution}.{key.SubjectName}.", $"{AnnotationTypeCodes.UnitOfExecution}.{key.SubjectName}."),
            ],
            AnnotationType.Execution =>
            [
                InPartition(PartitionKeys.GetResponsibility(project, key.ResponsibilityName), $"{AnnotationTypeCodes.UnitOfExecution}.{key.SubjectName}.{key.ResponsibilityName}.{key.ContextName}."),
            ],
            _ => [],
        };
    }

    private static DescendantScope InPartition(string partitionKey, params string[] keyPrefixes)
    {
        return new DescendantScope(partitionKey, partitionKey, keyPrefixes);
    }

    private static DescendantScope AcrossPartitions(string partitionPrefix, params string[] keyPrefixes)
    {
        return new DescendantScope(null, partitionPrefix, keyPrefixes);
    }
}
