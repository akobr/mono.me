namespace _42.Platform.Storyteller.Mcp.Hosting;

// Section Mcp:Actor:WorkloadIdentity: the platform identity token sent as actor_token, which the API's actor checks
// against its WorkloadIdentity (Issuer, Subject, Audience). No secret is stored anywhere.
public sealed class McpWorkloadIdentityOptions
{
    public McpWorkloadIdentityKind? Kind { get; set; }

    // ManagedIdentity: the resource the token is requested for, as {Audience}/.default: the App ID URI of an app
    // registration only Storyteller accepts, for example api://storyteller-token-exchange. Never api://AzureADTokenExchange
    // or an Azure resource, whose tokens are credentials there.
    // The API actor's WorkloadIdentity:Audience must equal the token's aud. With v1 tokens (the default, iss
    // https://sts.windows.net/{tid}/) that is this same App ID URI. With v2 tokens (requestedAccessTokenVersion 2, iss
    // https://login.microsoftonline.com/{tid}/v2.0) it is the app's client id, so use the client id here as well.
    public string? Audience { get; set; }

    // ManagedIdentity: the client id of a user-assigned identity; empty means the system-assigned one.
    public string? ClientId { get; set; }

    // TokenFile: the file that holds the token. It is read for every exchange, because the platform rotates it.
    public string? TokenFilePath { get; set; }

    // Set when any value is: the template in appsettings.json lists every key with an empty value.
    internal bool IsSet => Kind is not null
        || !string.IsNullOrWhiteSpace(Audience)
        || !string.IsNullOrWhiteSpace(ClientId)
        || !string.IsNullOrWhiteSpace(TokenFilePath);
}
