namespace _42.Platform.Storyteller.Accessing;

// Invitations of email addresses to organizations and projects.
// Administration calls carry the acting account; the service checks its role on the access point.
public interface IInvitationService
{
    Task<IReadOnlyList<Invitation>> GetInvitationsAsync(string accessPointKey, string actorId);

    Task<Invitation> CreateInvitationAsync(string accessPointKey, InvitationCreate model, string actorId, string? actorName);

    Task<Invitation> ResendInvitationAsync(string accessPointKey, string invitationId, string actorId);

    Task<Invitation> RevokeInvitationAsync(string accessPointKey, string invitationId, string actorId);

    // Pending, unexpired invitations for the caller's email.
    Task<IReadOnlyList<Invitation>> GetPendingInvitationsAsync(string email);

    // The invitee joins, and the access audit records it with the invitation and its sender; correlationId is the
    // request's (the Functions invocation id).
    Task<Account> AcceptInvitationAsync(string invitationId, InvitationIdentity invitee, string? correlationId = null);

    Task<Invitation> DeclineInvitationAsync(string invitationId, InvitationIdentity invitee);
}
