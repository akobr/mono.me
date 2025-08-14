using System.Diagnostics.CodeAnalysis;

namespace _42.Utils.Configuration.Substitute;

public interface ISubstitutionExecutor
{
    public bool TrySubstitute(string key, string value, [MaybeNullWhen(false)] out string newValue);
}
