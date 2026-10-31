namespace _42.Testing.UI.Automation.Runtime.UnitTests.Fakes;

// Waits in real time for what a background consumer does; fails after five seconds.
internal static class Eventually
{
    public static async Task TrueAsync(Func<bool> condition, string because)
    {
        var deadline = DateTime.UtcNow + TimeSpan.FromSeconds(5);
        while (!condition())
        {
            if (DateTime.UtcNow > deadline)
            {
                throw new TimeoutException($"Timed out waiting until {because}.");
            }

            await Task.Delay(5);
        }
    }

    // Gives a background task the chance to do what it should not do.
    public static Task SettleAsync()
    {
        return Task.Delay(100);
    }
}
