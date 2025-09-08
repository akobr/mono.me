namespace _42.Crumble;

[System.AttributeUsage(AttributeTargets.All, Inherited = false, AllowMultiple = true)]

public sealed class MessageActionAttribute : Attribute
{
    public string? MessageFilter { get; set; } = string.Empty;
}
