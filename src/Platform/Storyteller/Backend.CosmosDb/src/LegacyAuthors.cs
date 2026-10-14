using System;

namespace _42.Platform.Storyteller;

// Configurations, schemas, templates and views stored before the audit stamps name their author with a string:
// "account: {sub}", "application: {machineId}" or "system". They are not migrated; a read builds the stamp from
// these fields, and the next write stores the stamp instead.
internal static class LegacyAuthors
{
    private const string AccountPrefix = "account:";
    private const string ApplicationPrefix = "application:";

    public static AuditActor ToActor(string? author)
    {
        var text = author?.Trim();

        if (string.IsNullOrEmpty(text) || string.Equals(text, AuditActor.System.Id, StringComparison.OrdinalIgnoreCase))
        {
            return AuditActor.System;
        }

        if (text.StartsWith(AccountPrefix, StringComparison.OrdinalIgnoreCase))
        {
            return AuditActor.Account(text[AccountPrefix.Length..].Trim());
        }

        if (text.StartsWith(ApplicationPrefix, StringComparison.OrdinalIgnoreCase))
        {
            return AuditActor.Machine(text[ApplicationPrefix.Length..].Trim());
        }

        return AuditActor.Account(text);
    }

    public static AuditStamp ToStamp(string? author, DateTimeOffset at)
    {
        return AuditStamp.Of(ToActor(author), at);
    }
}
