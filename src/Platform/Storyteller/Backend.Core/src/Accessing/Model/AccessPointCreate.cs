namespace _42.Platform.Storyteller.Accessing.Model;

public record class AccessPointCreate
{
    public required string Organization { get; init; }

    public string? Project { get; init; }

    // The description of the new project; an organization created on the way gets none.
    public string? ProjectDescription { get; init; }

    // Not required: the API sets it from the caller, so clients do not send it.
    public string OwnerId { get; init; } = string.Empty;
}
