using _42.Platform.Storyteller.Sdk.Configuring;
using _42.Platform.Storyteller.Sdk.Credentials;

namespace _42.Platform.Storyteller.Sdk.Hosting.Informer;

// What a change source watches: the subscriptions of one responsibility in one context, in the organization, project
// and view of a key. Names are one key segment each and are compared ordinally.
internal sealed class SubscriptionScope
{
    private SubscriptionScope(FullKey responsibilityKey, string context)
    {
        ResponsibilityKey = responsibilityKey;
        Context = context;
    }

    // @{organization}.{project}.{view}.rst.{responsibility}
    public FullKey ResponsibilityKey { get; }

    public string Responsibility => ResponsibilityKey.Annotation.Name;

    public string Context { get; }

    // view: any full key of the view, as the options' Key; without one there is no organization, project and view to
    // watch in, which throws as who am I does without a key.
    public static SubscriptionScope Create(FullKey? view, string responsibility, string context)
    {
        ValidateName(responsibility, nameof(responsibility));
        ValidateName(context, nameof(context));

        if (view is null)
        {
            throw new StorytellerConfigurationException(
                $"The informer watches in the organization, project and view of the key of the process: set {StorytellerVariables.KEY} or {nameof(StorytellerConfigurationOptions)}.{nameof(StorytellerConfigurationOptions.Key)} to a full key of the view, for example @{{organization}}.{{project}}.{{view}}.rst.{responsibility}.");
        }

        return new SubscriptionScope(FullKey.Create(AnnotationKey.CreateResponsibility(responsibility), view), context);
    }

    // One segment of a key: not empty, without a dot.
    public static void ValidateName(string name, string parameterName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name, parameterName);

        if (name.Contains(Constants.DefaultKeySeparatorChar, StringComparison.Ordinal))
        {
            throw new ArgumentException($"'{name}' is not a name: a name is one segment of a key, without a dot.", parameterName);
        }
    }

    public FullKey ExecutionKey(string subject)
    {
        return FullKey.Create(AnnotationKey.CreateExecution(subject, Responsibility, Context), ResponsibilityKey);
    }

    public FullKey UsageKey(string subject)
    {
        return FullKey.Create(AnnotationKey.CreateUsage(subject, Responsibility), ResponsibilityKey);
    }

    public override string ToString()
    {
        return $"{ResponsibilityKey} in the context {Context}";
    }
}
