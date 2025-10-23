namespace _42.Crumble;

[AttributeUsage(AttributeTargets.Method, Inherited = false, AllowMultiple = true)]
public sealed class VolumeActionAttribute : ActionAttribute
{
    public string? VolumeKey { get; set; }

    public string? FilePathFilter { get; set; }
}
