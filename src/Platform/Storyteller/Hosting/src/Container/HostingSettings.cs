using _42.Platform.Storyteller.Sdk.Configuring.Settings;

namespace _42.Platform.Storyteller.Sdk.Hosting.Container;

// The settings Hosting read at registration, one per container: who am I, the catalog and the documents it registers
// share them, and with them one bound credential and its token cache. Its registration marks the container as set up.
internal sealed class HostingSettings
{
    public HostingSettings(StorytellerSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);

        Settings = settings;
    }

    public StorytellerSettings Settings { get; }
}
