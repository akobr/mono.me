namespace _42.Platform.Cli.Configuration;

// The editor that an edit opens, and why: the configured one, else the one of $VISUAL or $EDITOR as a custom command,
// else none, and sform asks. sform account settings shows the same resolution as the edit commands use.
public static class EditorResolution
{
    public static ResolvedEditor Resolve(EditorOptions configured, EnvironmentEditor environment)
    {
        if (configured.IsConfigured)
        {
            return new ResolvedEditor(configured, EditorOrigin.Configured, null);
        }

        if (environment.IsSet)
        {
            var options = new EditorOptions { EditorType = EditorType.Custom, CustomCommand = environment.Command };
            return new ResolvedEditor(options, EditorOrigin.Environment, environment.Variable);
        }

        return new ResolvedEditor(null, EditorOrigin.None, null);
    }
}
