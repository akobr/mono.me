namespace _42.Platform.Storyteller.Sdk.Configuring.References;

// A value with the shape of a secret reference that stays as written: its JSON path, and the host of a secret URI that
// a resolver declined (null without a resolver).
internal sealed record UnresolvedReference(string Path, string? Host);
