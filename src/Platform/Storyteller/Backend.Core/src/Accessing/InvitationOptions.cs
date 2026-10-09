namespace _42.Platform.Storyteller.Accessing;

public class InvitationOptions
{
    public const string SectionName = "Invitations";

    public int DefaultExpiresInDays { get; set; } = 7;

    // WorkOS accepts at most 30 days.
    public int MaxExpiresInDays { get; set; } = 30;
}
