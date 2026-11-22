namespace _42.Platform.Storyteller.Notifying;

// The accepted item, its view and who accepted it.
public sealed record ViewInboxActionContext(string Organization, string Project, string View, ViewInboxItem Item, AuditActor Actor);
