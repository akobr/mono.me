namespace _42.Crumble;

[AttributeUsage(AttributeTargets.All, Inherited = false, AllowMultiple = true)]
public sealed class MessageActionAttribute : ActionAttribute
{
    public string? QueueKey { get; set; }

    public string? MessageFilter { get; set; }
}
