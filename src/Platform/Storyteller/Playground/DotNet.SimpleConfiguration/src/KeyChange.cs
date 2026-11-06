namespace _42.Platform.Storyteller.Playground.DotNet.SimpleConfiguration;

public sealed record KeyChange(KeyChangeKind Kind, string Key, string? Before, string? After);
