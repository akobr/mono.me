namespace _42.Platform.Storyteller.Sdk.Credentials;

// What GET v1/auth/configuration tells a client-credentials machine; the rest of the answer is for users.
internal sealed record SignInDiscovery(string? Provider, string? AuthKitDomain);
