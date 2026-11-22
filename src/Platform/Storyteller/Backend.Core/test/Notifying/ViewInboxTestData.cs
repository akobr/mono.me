using System.Text;
using _42.Platform.Storyteller.Notifying;

namespace _42.Platform.Storyteller.Backend.Core.UnitTests.Notifying;

internal static class ViewInboxTestData
{
    public const string HandledAction = "GenerateSchema";

    // An id of the right shape that no test stores: the time of Start and 32 hex characters.
    public const string UnknownId = "202610010800000000000" + "00112233445566778899aabbccddeeff";

    public static readonly AuditActor Other = new() { Kind = AuditActorKind.Account, Id = "user_02", Name = "Grace" };

    // A push to "acme.main.default" by the platform: an action with the code of the tests' handler, or a notification
    // or a message about an export.
    public static ViewInboxPush Push(ViewInboxItemKind kind = ViewInboxItemKind.Notification, AccountRole role = AccountRole.Administrator)
    {
        var isAction = kind == ViewInboxItemKind.Action;

        return new ViewInboxPush
        {
            Organization = "acme",
            Project = "main",
            View = "default",
            Kind = kind,
            Topic = isAction ? "SchemaSuggestion" : ViewInboxTopics.ViewExport,
            Title = isAction ? "Generate a schema for 4 similar configurations" : "Export finished",
            MinimalRole = role,
            CreatedBy = AuditActor.System,
            Action = isAction ? HandledAction : null,
        };
    }

    // A continuation token built without the service: the base64url of the text.
    public static string Token(string text)
    {
        return Convert.ToBase64String(Encoding.UTF8.GetBytes(text)).TrimEnd('=').Replace('+', '-').Replace('/', '_');
    }
}
