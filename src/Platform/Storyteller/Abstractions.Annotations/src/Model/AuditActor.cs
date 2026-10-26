using System;
using Newtonsoft.Json;
using Newtonsoft.Json.Converters;

namespace _42.Platform.Storyteller;

[JsonConverter(typeof(StringEnumConverter))]
public enum AuditActorKind
{
    Account = 0,
    Machine,
    System,
}

// Who did something. Kind and Id are the reference: the account's subject (sub, the account id), the machine id,
// or "system". Name is a display snapshot taken when the change was made; it is never looked up and may be stale.
public record class AuditActor
{
    public required AuditActorKind Kind { get; init; }

    public required string Id { get; init; }

    public string? Name { get; init; }

    // The actor that acted for the account (the act.sub of a Storyteller user token, such as the MCP server), so a change
    // reads "Ada via mcp". Null for a direct call, and then left out of stored and returned documents.
    public string? Via { get; init; }

    public static AuditActor System { get; } = new() { Kind = AuditActorKind.System, Id = "system" };

    public static AuditActor Account(string id, string? name = null) => new() { Kind = AuditActorKind.Account, Id = id, Name = name };

    public static AuditActor Machine(string id, string? name = null) => new() { Kind = AuditActorKind.Machine, Id = id, Name = name };
}

// Who made a change and when. Created: the write that created the object; Modified: the write that produced the
// current state (or the listed version); Deleted: the soft delete, only while deleted.
public record class AuditStamp
{
    public required DateTimeOffset At { get; init; }

    public required AuditActor By { get; init; }

    public static AuditStamp Of(AuditActor by, DateTimeOffset at) => new() { At = at, By = by };
}
