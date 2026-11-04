namespace _42.Platform.Storyteller.Sdk.Hosting;

// One event of the subscriptions. Added when a subscription has its first good document; Changed when its execution,
// its usage or the data of its document changed; Removed when the informer removed it, with the subscription held
// before and Removal, which annotation ended it and why.
public sealed record StorytellerSubscriptionChange(
    StorytellerChangeKind Kind,
    StorytellerSubscription Subscription,
    StorytellerRemoval? Removal = null);
