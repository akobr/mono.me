using System.Diagnostics.CodeAnalysis;
using Newtonsoft.Json;

namespace _42.Platform.Storyteller.Transferring;

// The queue message of one job: where it is stored and what it does.
public sealed record class ConfigurationTransferMessage(string Organization, string Project, string View, ConfigurationTransferKind Kind, string Id)
{
    public string ToJson() => JsonConvert.SerializeObject(this);

    // False for a message that is not JSON or misses a field.
    public static bool TryParse(string? json, [NotNullWhen(true)] out ConfigurationTransferMessage? message)
    {
        message = null;

        if (string.IsNullOrWhiteSpace(json))
        {
            return false;
        }

        try
        {
            message = JsonConvert.DeserializeObject<ConfigurationTransferMessage>(json);
        }
        catch (JsonException)
        {
            return false;
        }

        if (message is null
            || string.IsNullOrEmpty(message.Organization)
            || string.IsNullOrEmpty(message.Project)
            || string.IsNullOrEmpty(message.View)
            || string.IsNullOrEmpty(message.Id)
            || !Enum.IsDefined(message.Kind))
        {
            message = null;
            return false;
        }

        return true;
    }
}
