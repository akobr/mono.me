namespace _42.Platform.Storyteller.ConfigurationFormats;

// One step from the root to a value: a property name, or an array index when Name is null (Index is -1 for a name).
public readonly record struct DocumentSegment(string? Name, int Index)
{
    public bool IsIndex => Name is null;

    public static DocumentSegment Property(string name)
    {
        return new DocumentSegment(name, -1);
    }

    public static DocumentSegment Item(int index)
    {
        return new DocumentSegment(null, index);
    }
}
