namespace _42.Platform.Storyteller.ConfigurationFormats;

// A variable of the env format: its name with the prefix, and its raw value, without the quoting of a .env file.
public sealed record EnvironmentVariable(string Name, string Value, DocumentValueKind Kind, string Path)
{
    // Never the value, which may be a secret.
    public override string ToString()
    {
        return $"{Name} ({Path})";
    }
}
