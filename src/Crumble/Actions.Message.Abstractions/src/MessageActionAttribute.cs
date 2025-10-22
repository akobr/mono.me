namespace _42.Crumble;

[System.AttributeUsage(AttributeTargets.All, Inherited = false, AllowMultiple = true)]

public sealed class MessageActionAttribute : Attribute
{
    public string? MessageQueueKey { get; set; }

    public string? MessageFilter { get; set; }
}
