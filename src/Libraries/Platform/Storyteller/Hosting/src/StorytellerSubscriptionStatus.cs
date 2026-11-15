namespace _42.Platform.Storyteller.Sdk.Hosting;

// Whether a subscription is in effect: the status of the annotations that a StorytellerStatusScope counts. Subscription:
// the key's annotation, its execution and its usage. Chain: the key's annotation and every ancestor of its chain.
public enum StorytellerSubscriptionStatus
{
    Active = 0,

    // One of them is disabled.
    Disabled,

    // One of them is valid from a later time.
    Scheduled,

    // One of them expired.
    Expired,

    // The key's annotation or its execution is deleted, or gone, in either scope.
    Deleted,

    // Everything that was read is active, but the credential may not read one that counts. Subscription: the usage (a
    // machine restricted to its execution), or the execution of a unit of execution (a machine restricted to that unit).
    // Chain: any ancestor.
    Unknown,
}
