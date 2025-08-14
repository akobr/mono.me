namespace _42.Utils.Configuration.Substitute;

public interface ISubstituteStrategy
{
    string? Substitute(string propertyPath);
}
