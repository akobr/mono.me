using System.Collections.Generic;

namespace _42.Platform.Storyteller;

public static class AnnotationHierarchy
{
    public static IReadOnlyList<(AnnotationKey Ancestor, string DescendantTypeCode)> GetAncestorSchemaSources(AnnotationKey key)
    {
        var results = new List<(AnnotationKey, string)>();

        switch (key.Type)
        {
            case AnnotationType.Responsibility:
            case AnnotationType.Subject:
                break;

            case AnnotationType.Unit:
                // unt.R.U → rst.R defines dt.unt
                results.Add((AnnotationKey.CreateResponsibility(key.ResponsibilityName), AnnotationTypeCodes.Unit));
                break;

            case AnnotationType.Context:
                // cnt.S.C → sbt.S defines dt.cnt
                results.Add((AnnotationKey.CreateSubject(key.SubjectName), AnnotationTypeCodes.Context));
                break;

            case AnnotationType.Usage:
                // usg.S.R → sbt.S defines dt.usg, rst.R defines dt.usg
                results.Add((AnnotationKey.CreateResponsibility(key.ResponsibilityName), AnnotationTypeCodes.Usage));
                results.Add((AnnotationKey.CreateSubject(key.SubjectName), AnnotationTypeCodes.Usage));
                break;

            case AnnotationType.Execution:
                // exe.S.R.C → sbt.S, rst.R, cnt.S.C, usg.S.R all define dt.exe
                results.Add((AnnotationKey.CreateResponsibility(key.ResponsibilityName), AnnotationTypeCodes.Execution));
                results.Add((AnnotationKey.CreateSubject(key.SubjectName), AnnotationTypeCodes.Execution));
                results.Add((AnnotationKey.CreateUsage(key.SubjectName, key.ResponsibilityName), AnnotationTypeCodes.Execution));
                results.Add((AnnotationKey.CreateContext(key.SubjectName, key.ContextName), AnnotationTypeCodes.Execution));
                break;

            case AnnotationType.UnitOfExecution:
                // uxe.S.R.C.U → sbt.S, rst.R, cnt.S.C, usg.S.R, unt.R.U, exe.S.R.C all define dt.uxe
                results.Add((AnnotationKey.CreateResponsibility(key.ResponsibilityName), AnnotationTypeCodes.UnitOfExecution));
                results.Add((AnnotationKey.CreateSubject(key.SubjectName), AnnotationTypeCodes.UnitOfExecution));
                results.Add((AnnotationKey.CreateUsage(key.SubjectName, key.ResponsibilityName), AnnotationTypeCodes.UnitOfExecution));
                results.Add((AnnotationKey.CreateContext(key.SubjectName, key.ContextName), AnnotationTypeCodes.UnitOfExecution));
                results.Add((AnnotationKey.CreateExecution(key.SubjectName, key.ResponsibilityName, key.ContextName), AnnotationTypeCodes.UnitOfExecution));
                results.Add((AnnotationKey.CreateUnit(key.ResponsibilityName, key.UnitName), AnnotationTypeCodes.UnitOfExecution));
                break;
        }

        return results;
    }
}
