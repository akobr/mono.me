namespace _42.Platform.Storyteller;

public record class InvitationCreate
{
    public required string Email { get; init; }

    public required AccountRole Role { get; init; }

    // 1 to 30, 7 when null.
    public int? ExpiresInDays { get; init; }
}
