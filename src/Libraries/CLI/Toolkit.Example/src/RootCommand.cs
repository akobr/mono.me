using McMaster.Extensions.CommandLineUtils;

namespace _42.CLI.Toolkit.Example;

[Subcommand(typeof(ExampleParentCommand))]
[Command("toolkit", Description = "Examples of 42.CLI.Toolkit.")]
public class RootCommand : IAsyncCommand
{
    private readonly CommandLineApplication _application;

    public RootCommand(CommandLineApplication application)
    {
        _application = application;
    }

    [VersionOption("-v|--version", "", Description = "Display version of this tool.")]
    public bool IsVersionRequested { get; set; }

    public Task<int> OnExecuteAsync()
    {
        _application.ShowHelp();
        return Task.FromResult(0);
    }
}
