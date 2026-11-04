using System.Text;
using _42.Platform.Storyteller.Sdk.Configuring;

namespace _42.Platform.Storyteller.Sdk.Hosting;

// A subscription without a first good document: no event yet, and not among the subscriptions. Key: the full key of its
// execution. LastError: the failure of its latest read.
public sealed record StorytellerPendingSubscription(string Key, StorytellerConfigurationException? LastError)
{
    // For ToString(): the error by its message, which names keys and statuses only. The whole exception would add inner
    // exceptions that Hosting does not vouch for.
    private bool PrintMembers(StringBuilder builder)
    {
        builder
            .Append("Key = ").Append(Key)
            .Append(", LastError = ").Append(LastError?.Message);
        return true;
    }
}
