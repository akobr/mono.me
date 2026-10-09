using System;

namespace _42.Platform.Storyteller;

// An invitation of an email address to an organization or project with a role.
public record class Invitation
{
    public required string Id { get; init; }

    public required string AccessPointKey { get; init; }

    // Normalized to lower case.
    public required string Email { get; init; }

    public required AccountRole Role { get; init; }

    public required InvitationStatus Status { get; init; }

    public required string InvitedById { get; init; }

    public string? InvitedByName { get; init; }

    public required DateTimeOffset CreatedAt { get; init; }

    public required DateTimeOffset ExpiresAt { get; init; }

    public string? AcceptedById { get; init; }

    public DateTimeOffset? RespondedAt { get; init; }

    // Id of the invitation at the identity provider that delivered the email (WorkOS).
    public string? ExternalInvitationId { get; init; }

    // False when no email was sent: the provider cannot send invitations, or sending failed.
    // The invitee can still accept after signing in; share the link manually.
    public bool IsEmailSent { get; init; }
}
