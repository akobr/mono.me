namespace _42.Platform.Storyteller.KeyValues.Protection;

// The project a key-encryption key belongs to.
public sealed record KeyValueScope(string Organization, string Project);

// The place of one stored version; the ciphertext is bound to it. Key is the normalized (lower-cased) key.
public sealed record KeyValueIdentity(string Organization, string Project, string View, string Key, ulong Version)
{
    public KeyValueScope Scope => new(Organization, Project);
}
