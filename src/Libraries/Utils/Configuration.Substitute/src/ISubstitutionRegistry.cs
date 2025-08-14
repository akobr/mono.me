namespace _42.Utils.Configuration.Substitute;

public interface ISubstitutionRegistry
{
    void RegisterSubstitution(string? sourceKey, ISubstituteStrategy strategy);
}
