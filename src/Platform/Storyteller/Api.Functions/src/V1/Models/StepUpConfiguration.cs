namespace _42.Platform.Storyteller.Api.V1.Models;

// The step-up of users in this deployment (Auth:StepUp): whether secrets, role changes and machine credentials need a
// recent sign-in, and how old it may be, in seconds. A client may tell a stale session before it asks; the API decides.
public record class StepUpConfiguration
{
    public required bool Enabled { get; init; }

    public required long MaxAge { get; init; }
}
