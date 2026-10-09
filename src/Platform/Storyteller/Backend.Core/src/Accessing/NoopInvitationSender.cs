namespace _42.Platform.Storyteller.Accessing;

// Used when the identity provider cannot send invitations (Entra ID, or AuthKit without a management key).
// Administrators share the invitation link themselves.
public sealed class NoopInvitationSender : IInvitationSender
{
    public Task<string?> SendAsync(Invitation invitation, CancellationToken cancellationToken = default)
    {
        return Task.FromResult<string?>(null);
    }

    public Task RevokeAsync(string externalInvitationId, CancellationToken cancellationToken = default)
    {
        return Task.CompletedTask;
    }
}
