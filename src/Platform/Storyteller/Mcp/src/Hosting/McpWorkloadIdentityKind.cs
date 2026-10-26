namespace _42.Platform.Storyteller.Mcp.Hosting;

// Where the platform identity token of the actor comes from.
public enum McpWorkloadIdentityKind
{
    // An Azure managed identity, asked through Azure.Identity.
    ManagedIdentity = 0,

    // A token file the platform projects and rotates: a Kubernetes service account token projected with Storyteller's
    // own audience (not AZURE_FEDERATED_TOKEN_FILE, whose audience is Entra's).
    TokenFile,
}
