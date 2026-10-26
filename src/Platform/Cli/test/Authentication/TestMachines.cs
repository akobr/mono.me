using System;
using System.Collections.Generic;
using System.IO.Abstractions;
using System.Net.Http;
using System.Threading;
using _42.Platform.Cli.Authentication;
using _42.Platform.Storyteller.Sdk.Credentials;
using _42.Platform.Storyteller.Sdk.Resilience;

namespace _42.Platform.Cli.UnitTests.Authentication;

// MachineAuthenticationService with the token requests of its SDK credential on a scripted handler: one answer per
// request, as there is no retry, and no attempt timeout.
internal static class TestMachines
{
    public static MachineCredentialHooks Hooks(HttpMessageHandler tokenRequests, StorytellerResilienceOptions? resilience = null)
    {
        return new MachineCredentialHooks
        {
            Decorate = credential => credential.WithPrimaryHandler(tokenRequests),
            Resilience = resilience ?? new StorytellerResilienceOptions { MaxRetryAttempts = 0, AttemptTimeout = Timeout.InfiniteTimeSpan },
        };
    }

    public static MachineAuthenticationService Create(
        IReadOnlyDictionary<string, string> environment,
        IFileSystem fileSystem,
        HttpMessageHandler tokenRequests,
        TimeProvider? time = null)
    {
        return Create(Settings(environment), fileSystem, Hooks(tokenRequests), time);
    }

    public static MachineAuthenticationService Create(
        MachineSignInSettings settings,
        IFileSystem fileSystem,
        MachineCredentialHooks hooks,
        TimeProvider? time = null)
    {
        return new MachineAuthenticationService(settings, fileSystem, time ?? TimeProvider.System, hooks);
    }

    public static MachineSignInSettings Settings(IReadOnlyDictionary<string, string> environment)
    {
        return MachineSignInSettings.FromEnvironment(name => environment.GetValueOrDefault(name));
    }
}
