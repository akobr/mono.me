namespace _42.Crumble.Playground.Examples;

public class ActionBasedCrumbs
{
    [TimeAction("*/5 * * * *", TimeZone = "UTC")] // Every 5 minutes
    [Crumb]
    public void Timed(DateTime triggerUtcTime)
    {
        Console.WriteLine($"Timed based crumb executed at {DateTime.UtcNow} and triggered at {triggerUtcTime} (UTC).");
    }

    [MessageAction]
    [Crumb]
    public void Messaged(string message)
    {
        Console.WriteLine("Message received.");
        Console.WriteLine(message);
    }

    [MessageAction]
    [Crumb]
    public void MessagedWithModel(MessageModel message)
    {
        Console.WriteLine($"Message wrapped in model received: {message.MessageId}");
        Console.WriteLine(message.MessageText);
    }

    [VolumeAction]
    [Crumb]
    public void Filed(string filePath)
    {
        Console.WriteLine($"File received: {filePath}");
    }
}
