namespace _42.Platform.Storyteller.Playground.DotNet.SimpleConfiguration;

// The typed options an application binds. A value that does not convert, such as a batchSize of abc, is a document that
// does not bind: the provider rejects it and restores the previous one.
public sealed class PlaygroundOptions
{
    public string? Greeting { get; set; }

    public int BatchSize { get; set; }

    public List<string> Tags { get; set; } = [];
}
