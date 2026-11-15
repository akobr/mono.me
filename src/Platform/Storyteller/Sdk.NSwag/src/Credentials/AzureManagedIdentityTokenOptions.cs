namespace _42.Platform.Storyteller.Sdk.Credentials;

// The platform token of a Federated machine from the managed identity of its Azure host: App Service, Functions and
// Container Apps (IDENTITY_ENDPOINT and IDENTITY_HEADER), otherwise IMDS of virtual machines and scale sets.
public sealed record AzureManagedIdentityTokenOptions
{
    // The identifier URI of the app registration that stands for Storyteller, api://<appId>.
    public required string Resource { get; init; }

    // The client id of a user-assigned identity; the system-assigned one when null.
    public string? ManagedIdentityClientId { get; init; }
}
