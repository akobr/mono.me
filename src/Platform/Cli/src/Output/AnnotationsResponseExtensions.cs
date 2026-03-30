using ApiSdk.Models;

namespace _42.Platform.Cli.Output;

public static class AnnotationsResponseExtensions
{
    public static string GetCount(this AnnotationsResponse @this)
    {
        return string.IsNullOrEmpty(@this.ContinuationToken)
            ? @this.Count.ToString()!
            : $"{@this.Count}+";
    }

    public static string GetCount(this AnnotationsResponse_Subject @this)
    {
        return string.IsNullOrEmpty(@this.ContinuationToken)
            ? @this.Count.ToString()!
            : $"{@this.Count}+";
    }

    public static string GetCount(this AnnotationsResponse_Responsibility @this)
    {
        return string.IsNullOrEmpty(@this.ContinuationToken)
            ? @this.Count.ToString()!
            : $"{@this.Count}+";
    }

    public static string GetCount(this AnnotationsResponse_Usage @this)
    {
        return string.IsNullOrEmpty(@this.ContinuationToken)
            ? @this.Count.ToString()!
            : $"{@this.Count}+";
    }

    public static string GetCount(this AnnotationsResponse_Context @this)
    {
        return string.IsNullOrEmpty(@this.ContinuationToken)
            ? @this.Count.ToString()!
            : $"{@this.Count}+";
    }

    public static string GetCount(this AnnotationsResponse_Execution @this)
    {
        return string.IsNullOrEmpty(@this.ContinuationToken)
            ? @this.Count.ToString()!
            : $"{@this.Count}+";
    }
}
