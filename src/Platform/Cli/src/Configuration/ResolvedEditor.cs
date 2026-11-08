namespace _42.Platform.Cli.Configuration;

// Options is null only for EditorOrigin.None; Variable names VISUAL or EDITOR for EditorOrigin.Environment.
public sealed record ResolvedEditor(EditorOptions? Options, EditorOrigin Origin, string? Variable);
