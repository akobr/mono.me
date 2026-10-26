namespace _42.Platform.Storyteller.Accessing;

// How an actor of the user token exchange proves itself; exactly one member is set.
public class ActorAuthenticationOptions
{
    // An actor_token from the actor's platform identity (an Azure managed identity, a Kubernetes service account, a
    // GitHub OIDC token). No secret is stored anywhere.
    public ActorWorkloadIdentityOptions? WorkloadIdentity { get; set; }

    // An RFC 7523 client_assertion (private_key_jwt) signed with the actor's published keys.
    public ActorClientAssertionOptions? ClientAssertion { get; set; }

    // The hex SHA-256 hash of a client_secret (client_secret_post). Local development only: refused at start outside the
    // Development environment.
    public string? ClientSecretHash { get; set; }
}
