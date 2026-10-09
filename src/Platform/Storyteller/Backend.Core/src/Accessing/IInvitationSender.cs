namespace _42.Platform.Storyteller.Accessing;

// Delivers invitation emails through the identity provider. Storyteller keeps the invitation itself;
// the provider only sends the email. Every method returns null when nothing was sent.
public interface IInvitationSender
{
    // The provider's invitation id.
    Task<string?> SendAsync(Invitation invitation, CancellationToken cancellationToken = default);

    Task RevokeAsync(string externalInvitationId, CancellationToken cancellationToken = default);
}
