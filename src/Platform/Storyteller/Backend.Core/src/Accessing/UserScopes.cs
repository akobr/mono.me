namespace _42.Platform.Storyteller.Accessing;

// The scope of user tokens. It is not a machine scope (MachineScopes): a token that carries it acts for its user, within
// the user's roles.
public static class UserScopes
{
    public const string Impersonation = "User.Impersonation";
}
