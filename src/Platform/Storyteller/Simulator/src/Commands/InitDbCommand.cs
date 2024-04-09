using System.Threading.Tasks;
using _42.CLI.Toolkit;
using _42.Platform.Storyteller.Simulator.Logic;
using McMaster.Extensions.CommandLineUtils;

namespace _42.Platform.Storyteller.Simulator.Commands;

[Command("2sInitDb", Description = "Initialize database for 2S platform.")]
public class InitDbCommand : IAsyncCommand
{
    private readonly CoreDbStructureBuilder _coreOrgBuilder;
    private readonly GetBaseAnnotationsReview _review;

    public InitDbCommand(
        CoreDbStructureBuilder coreOrgBuilder,
        GetBaseAnnotationsReview review)
    {
        _coreOrgBuilder = coreOrgBuilder;
        _review = review;
    }

    [VersionOption("-v|--version", "", Description = "Display version of this tool.")]
    public bool IsVersionRequested { get; set; }

    public async Task<int> OnExecuteAsync()
    {
        await _coreOrgBuilder.BuildAsync();
        await _review.RenderReview();
        return ExitCodes.SUCCESS;
    }
}
