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

// Who made a change. Id is the account's subject (sub) or the machine id; Name is a display snapshot from the token.
public record class AuditActor
{
    public required AuditActorKind Kind { get; init; }

    public required string Id { get; init; }

    public string? Name { get; init; }

    public static AuditActor System { get; } = new() { Kind = AuditActorKind.System, Id = "system" };
}

public record class AuditStamp
{
    public required DateTimeOffset At { get; init; }

    public required AuditActor By { get; init; }
}
