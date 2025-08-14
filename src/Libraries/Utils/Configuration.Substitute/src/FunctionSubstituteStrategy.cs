using System;

namespace _42.Utils.Configuration.Substitute;

public class FunctionSubstituteStrategy(Func<string, string?> substituteFunction)
    : ISubstituteStrategy
{
    public string? Substitute(string propertyPath)
    {
        return substituteFunction(propertyPath);
    }
}
