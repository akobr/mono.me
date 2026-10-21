namespace _42.Platform.Storyteller.Accessing.Model;

// The new description of a project; null or blank clears it.
public record class AccessPointUpdate
{
    public string? Description { get; init; }
}
