using Microsoft.Extensions.Configuration;

namespace _42.Utils.Configuration.Substitute.Referencing;

public class ReferenceSubstituteStrategy(IConfiguration source) : ISubstituteStrategy
{
    public string? Substitute(string propertyPath)
    {
        return source[propertyPath];
    }
}
