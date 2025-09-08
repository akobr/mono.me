namespace _42.Crumble;

[AttributeUsage(AttributeTargets.Method, Inherited = false, AllowMultiple = true)]
public sealed class CrumbAttribute : Attribute
{
    public string? Key { get; set; }

    public bool IsSingleAndSynchronized { get; set; }
}
