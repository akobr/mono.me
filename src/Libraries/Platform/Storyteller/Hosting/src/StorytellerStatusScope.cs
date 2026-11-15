namespace _42.Platform.Storyteller.Sdk.Hosting;

// Which annotations a subscription status counts.
public enum StorytellerStatusScope
{
    // The key's annotation, its execution and its usage: today's rule.
    Subscription = 0,

    // The key's annotation and every ancestor of its chain.
    Chain,
}
