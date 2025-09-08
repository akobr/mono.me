namespace _42.Crumble.Playground.Examples;

public class TimeBasedCrumbs
{
    [TimeAction("5 * * * *")] // Every 5 minutes
    [Crumb]
    public void TimedBased()
    {
        Console.WriteLine("Timed based crumb executed at: " + DateTime.Now);
    }
}
