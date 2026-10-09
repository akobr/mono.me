using System.Text.Json.Serialization;

namespace _42.Platform.Storyteller;

// Body of POST /user_management/invitations. Without organization_id it invites to the application.
public sealed record WorkOsInvitationCreate(
    [property: JsonPropertyName("email")] string Email,
    [property: JsonPropertyName("expires_in_days")] int ExpiresInDays,
    [property: JsonPropertyName("inviter_user_id")]
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    string? InviterUserId);

// The subset of the WorkOS invitation object Storyteller reads.
public sealed record WorkOsInvitation(
    [property: JsonPropertyName("id")] string Id,
    [property: JsonPropertyName("email")] string? Email,
    [property: JsonPropertyName("state")] string? State,
    [property: JsonPropertyName("expires_at")] DateTimeOffset? ExpiresAt,
    [property: JsonPropertyName("accept_invitation_url")] string? AcceptInvitationUrl);
