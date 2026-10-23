namespace _42.Platform.Storyteller.Sdk.Configuring;

// The platform's forms of a secret reference, as a key/value pair of a reference content type stores them.
public enum SecretReferenceForm
{
    // kv://{vault}/{name}[/{version}]: a vault by name, whose host the resolver chooses.
    Shorthand = 0,

    // https://{host}/secrets/{name}[/{version}][/]: a vault secret URI.
    SecretUri,

    // App Configuration's Key Vault reference: the JSON object {"uri": "<secret URI>"}.
    AppConfiguration,
}
