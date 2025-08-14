using System.Collections.Generic;
using System.Linq;

namespace _42.Utils.Configuration.Substitute.Aggregate;

public class AggregationSubstituteStrategy : ISubstituteStrategy, ISubstituteAggregation
{
    private readonly List<ISubstituteStrategy> _strategies = new();

    public string? Substitute(string propertyPath)
    {
        return _strategies
            .Select(strategy => strategy.Substitute(propertyPath))
            .FirstOrDefault(value => value is not null);
    }

    public void AddSubstitution(ISubstituteStrategy strategy)
    {
        _strategies.Add(strategy);
    }
}
