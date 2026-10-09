namespace _42.Platform.Storyteller;

// Stable values of ErrorResponse.ErrorCode. Clients branch on them, so existing values never change.
public static class ErrorCodes
{
    public const string AccessDenied = "AccessDenied";
    public const string NotFound = "NotFound";
    public const string Conflict = "Conflict";

    public const string AccountExists = "AccountExists";
    public const string AccessPointExists = "AccessPointExists";
    public const string LastOwner = "LastOwner";
    public const string ElevatedRole = "ElevatedRole";

    public const string PatchInvalid = "PatchInvalid";
    public const string PatchTestFailed = "PatchTestFailed";
}
