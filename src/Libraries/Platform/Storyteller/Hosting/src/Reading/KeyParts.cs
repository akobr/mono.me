namespace _42.Platform.Storyteller.Sdk.Hosting.Reading;

// The names a key carries besides its type code.
[Flags]
internal enum KeyParts
{
    None = 0,
    Subject = 1,
    Responsibility = 2,
    Context = 4,
    Unit = 8,
}
