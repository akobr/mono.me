namespace _42.Platform.Storyteller.Mcp.Tools;

// Descriptions shared by the tools of the values store (docs/Platform/Storyteller/key-values.md).
public static class KeyValueText
{
    public const string Key =
        "The key of the pair: segments of letters, digits and _ that start with a letter or _, joined by dots, at most 128 "
        + "characters (db.password); unique case-insensitively.";

    public const string Version = "A stored version number of the pair, from list_key_value_versions.";

    public const string Keys = "Up to 100 keys; give keys or keyPrefix, not both.";

    public const string KeyPrefix = "Every key starting with this prefix (case-insensitive); an empty string means every key of the view. Give keys or keyPrefix, not both.";

    public const string Outcomes = "Each key gets its own outcome in Items: Key, Outcome, Version, ErrorCode, Message.";

    public const string Levels = "Public, Standard, Hsm, Byok or Hyok";
}
