namespace _42.Platform.Storyteller.Sdk.Hosting;

// The options of who am I: services.Configure<StorytellerExecutionContextOptions>(…).
public sealed class StorytellerExecutionContextOptions
{
    // How old the snapshot may get before a call of GetAsync refreshes it in the background. Zero or infinite: only
    // RefreshAsync reads again.
    public TimeSpan RefreshInterval { get; set; } = TimeSpan.FromMinutes(5);
}
