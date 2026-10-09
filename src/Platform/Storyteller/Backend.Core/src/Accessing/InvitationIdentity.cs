namespace _42.Platform.Storyteller.Accessing;

// The signed-in user answering an invitation, as proven by the bearer token.
public sealed record InvitationIdentity(
    string AccountId,
    string? Email,
    bool IsEmailVerified,
    string? UserName,
    string? Name);
