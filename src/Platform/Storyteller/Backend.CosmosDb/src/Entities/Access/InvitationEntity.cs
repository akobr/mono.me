using System;
using System.Text.Json.Serialization;
using Newtonsoft.Json;

namespace _42.Platform.Storyteller.Entities.Access;

// Stored next to accounts and access points, in the main partition of the core container.
public record class InvitationEntity
{
    public const string IdPrefix = "inv.";

    public string PartitionKey => "access";

    [JsonProperty("id")]
    [JsonPropertyName("id")]
    public string Id => $"{IdPrefix}{InvitationId}";

    public required string InvitationId { get; init; }

    public required string AccessPointKey { get; init; }

    public required string Email { get; init; }

    public required AccountRole Role { get; init; }

    // Pending, Accepted, Declined or Revoked. Expired is computed on read.
    public required InvitationStatus Status { get; init; }

    public required string InvitedById { get; init; }

    public string? InvitedByName { get; init; }

    public required DateTimeOffset CreatedAt { get; init; }

    public required DateTimeOffset ExpiresAt { get; init; }

    public string? AcceptedById { get; init; }

    public DateTimeOffset? RespondedAt { get; init; }

    public string? ExternalInvitationId { get; init; }

    public bool IsEmailSent { get; init; }

    public Invitation ToInvitation(DateTimeOffset now) => new()
    {
        Id = InvitationId,
        AccessPointKey = AccessPointKey,
        Email = Email,
        Role = Role,
        Status = Status == InvitationStatus.Pending && ExpiresAt <= now ? InvitationStatus.Expired : Status,
        InvitedById = InvitedById,
        InvitedByName = InvitedByName,
        CreatedAt = CreatedAt,
        ExpiresAt = ExpiresAt,
        AcceptedById = AcceptedById,
        RespondedAt = RespondedAt,
        ExternalInvitationId = ExternalInvitationId,
        IsEmailSent = IsEmailSent,
    };
}
