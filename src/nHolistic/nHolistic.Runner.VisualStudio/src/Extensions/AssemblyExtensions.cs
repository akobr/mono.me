using System.Reflection;
using System.Text;

namespace _42.nHolistic.Runner.VisualStudio;

public static class AssemblyExtensions
{
    public static string GetVersionInfo(this Assembly @this)
    {
        var fileVersionAttribute = @this.GetCustomAttribute<AssemblyFileVersionAttribute>();
        var informationalVersionAttribute = @this.GetCustomAttribute<AssemblyInformationalVersionAttribute>();
        var result = new StringBuilder();

        if (fileVersionAttribute is not null)
        {
            result.Append(fileVersionAttribute.Version);
        }
        else
        {
            result.Append(@this.GetName().Version?.ToString() ?? "42-the-answer");
        }

        if (informationalVersionAttribute is not null)
        {
            result.Append($" ({informationalVersionAttribute.InformationalVersion})");
        }

        return result.ToString();
    }
}
