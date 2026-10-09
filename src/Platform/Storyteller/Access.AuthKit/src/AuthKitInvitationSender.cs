using _42.Platform.Storyteller.Accessing;

namespace _42.Platform.Storyteller;

// Delivers Storyteller invitations as WorkOS application invitations (no WorkOS organization).
// WorkOS sends the email; its link signs the invitee up even when AuthKit sign-up is disabled.
// Storyteller still matches the invitation's email against the signed-in user on acceptance,
// because a WorkOS application invitation can be accepted with any email.
public sealed class AuthKitInvitationSender : IInvitationSender
{
    // WorkOS accepts 1 to 30 days.
    private const int MaxExpiresInDays = 30;
    private const string UserIdPrefix = "user_";

    private readonly WorkOsManagementClient _client;

    public AuthKitInvitationSender(WorkOsManagementClient client)
    {
        _client = client;
    }

    public async Task<string?> SendAsync(Invitation invitation, CancellationToken cancellationToken = default)
    {
        if (!_client.IsConfigured)
        {
            return null;
        }

        var days = (int)Math.Ceiling((invitation.ExpiresAt - invitation.CreatedAt).TotalDays);
        var inviter = invitation.InvitedById.StartsWith(UserIdPrefix, StringComparison.Ordinal)
            ? invitation.InvitedById
            : null;

        var created = await _client.SendInvitationAsync(
            new WorkOsInvitationCreate(invitation.Email, Math.Clamp(days, 1, MaxExpiresInDays), inviter),
            cancellationToken);
        return created.Id;
    }

    public async Task RevokeAsync(string externalInvitationId, CancellationToken cancellationToken = default)
    {
        if (!_client.IsConfigured)
        {
            return;
        }

        await _client.RevokeInvitationAsync(externalInvitationId, cancellationToken);
    }
}
