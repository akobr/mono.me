using _42.Platform.Sdk.Model;

namespace _42.Platform.Cli.Output;

public static class AnnotationsResponseExtensions
{
    public static string GetCount(this AnnotationsResponse @this)
    {
        return string.IsNullOrEmpty(@this.ContinuationToken)
            ? @this.Count.ToString()
            : $"{@this.Count}+";
    }
}
