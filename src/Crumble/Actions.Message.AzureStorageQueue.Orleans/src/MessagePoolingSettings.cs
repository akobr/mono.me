namespace _42.Crumble;

public class MessagePoolingSettings
{
    public TimeSpan PoolingMaxInterval { get; set; } = TimeSpan.FromMinutes(15);

    public TimeSpan PoolingMinInterval { get; set; } = TimeSpan.FromMilliseconds(100);

    public int BatchSize { get; set; } = 32;

    public TimeSpan VisibilityTimeout { get; set; } = TimeSpan.FromMinutes(15);

    public int MaxDequeueCount { get; set; } = 5;
}
