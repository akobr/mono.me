using System;
using _42.Platform.Storyteller.Sdk.Credentials;
using _42.Platform.Storyteller.Sdk.Resilience;

namespace _42.Platform.Cli.Authentication;

// The seam of tests: MachineAuthenticationService applies it to every credential it binds, so token requests go to a
// scripted handler (Decorate = c => c.WithPrimaryHandler(handler)) without retries or attempt timeouts.
internal sealed class MachineCredentialHooks
{
    public static MachineCredentialHooks None { get; } = new();

    public Func<StorytellerCredential, StorytellerCredential>? Decorate { get; init; }

    public StorytellerResilienceOptions? Resilience { get; init; }
}
