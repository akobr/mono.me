namespace _42.Crumble;

[AttributeUsage(AttributeTargets.Method, Inherited = false, AllowMultiple = true)]
public sealed class TimeActionAttribute(string cron) : ActionAttribute
{
    public string Cron { get; set; } = cron ?? throw new ArgumentNullException(nameof(cron), "Cron expression cannot be null.");

    public string? TimeZone { get; set; }
}
