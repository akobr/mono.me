namespace _42.Platform.Storyteller.Sdk.Hosting;

// Whether a subscription is in effect: the status of the key's annotation, its execution and its usage.
public enum StorytellerSubscriptionStatus
{
    Active = 0,

    // One of them is disabled.
    Disabled,

    // One of them is valid from a later time.
    Scheduled,

    // One of them expired.
    Expired,

    // The key's annotation or its execution is deleted, or gone.
    Deleted,

    // Everything that was read is active, but the credential may not read the usage (a machine restricted to its
    // execution).
    Unknown,
}
