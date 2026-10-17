namespace _42.Platform.Storyteller;

public sealed record StorytellerToken(string AccessToken, DateTimeOffset ExpiresAt, int ExpiresIn);
