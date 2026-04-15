namespace _42.Platform.Cli.Configuration;

public enum EditorType
{
    VsCode,
    Neovim,
    Vim,
    Custom,
}

public class EditorOptions
{
    public EditorType? EditorType { get; set; }

    public string? CustomCommand { get; set; }

    public bool IsConfigured => EditorType.HasValue;
}
