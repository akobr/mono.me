namespace _42.Platform.Cli.Configuration;

public enum EditorOrigin
{
    // No editor: sform asks for one and saves the choice.
    None,

    // The settings file or a legacy editor.config.json.
    Configured,

    // $VISUAL or $EDITOR, for the run only.
    Environment,
}
