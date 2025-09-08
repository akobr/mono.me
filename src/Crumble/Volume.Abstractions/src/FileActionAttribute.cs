namespace _42.Crumble;

[System.AttributeUsage(AttributeTargets.Method, Inherited = false, AllowMultiple = true)]

public sealed class FileActionAttribute : Attribute
{
    public string? FilePathFilter { get; set; } = string.Empty;
}
