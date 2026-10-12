using Newtonsoft.Json;
using Newtonsoft.Json.Converters;

namespace _42.Platform.Storyteller;

// How the value of a key/value pair is protected. Everything except Public is a secret.
[JsonConverter(typeof(StringEnumConverter))]
public enum KeyValueProtection
{
    // Plaintext in Cosmos DB, visible to every reader of the project.
    Public = 0,

    // Envelope-encrypted in Cosmos DB; the data key is wrapped by a software key of the project.
    Standard = 1,

    // Envelope-encrypted in Cosmos DB; the data key is wrapped by an HSM key of the project.
    Hsm = 2,

    // Envelope-encrypted in Cosmos DB; the data key is wrapped by the customer's own key.
    Byok = 3,

    // Encrypted by the client for the project's recipients; the platform stores an opaque document.
    Hyok = 4,
}

public static class KeyValueProtectionExtensions
{
    public static bool IsSecret(this KeyValueProtection protection)
    {
        return protection != KeyValueProtection.Public;
    }

    // Levels whose plaintext the server can read and write again under another level.
    public static bool IsServerSideEncrypted(this KeyValueProtection protection)
    {
        return protection is KeyValueProtection.Standard or KeyValueProtection.Hsm or KeyValueProtection.Byok;
    }
}
