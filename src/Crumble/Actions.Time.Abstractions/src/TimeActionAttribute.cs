namespace _42.Crumble;

[AttributeUsage(AttributeTargets.Method, Inherited = false, AllowMultiple = true)]
public sealed class TimeActionAttribute(string cron) : Attribute
{
    public string Cron { get; set; } = cron ?? throw new ArgumentNullException(nameof(cron), "Cron expression cannot be null.");
}
