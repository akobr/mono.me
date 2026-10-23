namespace _42.Platform.Storyteller.Sdk.Configuring;

// The refreshers of every Storyteller source in the application's IConfiguration, found again on each access, so a
// rebuilt configuration is followed.
public interface IStorytellerConfigurationRefresherProvider
{
    IReadOnlyList<IStorytellerConfigurationRefresher> Refreshers { get; }
}
