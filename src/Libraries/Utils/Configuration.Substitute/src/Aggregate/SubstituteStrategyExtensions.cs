namespace _42.Utils.Configuration.Substitute.Aggregate;

public static class SubstituteStrategyExtensions
{
    public static ISubstituteStrategy AggregateWith(this ISubstituteStrategy @this, ISubstituteStrategy strategy)
    {
        if (@this is ISubstituteAggregation existingAggregation)
        {
            existingAggregation.AddSubstitution(strategy);
            return @this;
        }

        var newAggregation = new AggregationSubstituteStrategy();
        newAggregation.AddSubstitution(@this);
        newAggregation.AddSubstitution(strategy);
        return newAggregation;
    }
}
