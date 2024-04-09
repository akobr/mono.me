using System;
using _42.Platform.Storyteller.Entities.Annotations;
using AutoMapper;

namespace _42.Platform.Storyteller;

public static class MapperExtensions
{
    public static AnnotationEntity MapToEntity(this IMapper @this, IAnnotation annotation)
    {
        return annotation switch
        {
            Responsibility responsibility => @this.Map<ResponsibilityEntity>(responsibility),
            Usage responsibility => @this.Map<UsageEntity>(responsibility),
            Execution execution => @this.Map<ExecutionEntity>(execution),
            Subject subject => @this.Map<SubjectEntity>(subject),
            Context context => @this.Map<ContextEntity>(context),
            Unit unit => @this.Map<UnitEntity>(unit),
            _ => throw new NotSupportedException($"Unsupported annotation type: {annotation.GetType().Name}"),
        };
    }

    public static Annotation MapFromEntity(this IMapper @this, AnnotationEntity annotation)
    {
        return annotation switch
        {
            ResponsibilityEntity responsibility => @this.Map<Responsibility>(responsibility),
            UsageEntity responsibility => @this.Map<Usage>(responsibility),
            ExecutionEntity execution => @this.Map<Execution>(execution),
            SubjectEntity subject => @this.Map<Subject>(subject),
            ContextEntity context => @this.Map<Context>(context),
            UnitEntity unit => @this.Map<Unit>(unit),
            _ => throw new NotSupportedException($"Unsupported annotation entity type: {annotation.GetType().Name}"),
        };
    }
}
