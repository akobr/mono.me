using System.Drawing;
using System.Threading.Tasks;
using _42.CLI.Toolkit;
using _42.Platform.Storyteller.DbCreator.Configuration;
using _42.Platform.Storyteller.DbCreator.DataSets;
using _42.Platform.Storyteller.DbCreator.Logic;
using McMaster.Extensions.CommandLineUtils;
using Microsoft.Extensions.Options;
using Console = Colorful.Console;

namespace _42.Platform.Storyteller.DbCreator.Commands;

[Command("2sInitDb", Description = "Initialize the database of the 2S platform: the containers, the access of the base owner and the data set core. The subcommands seed and list data sets.")]
[Subcommand(typeof(SeedCommand), typeof(SetsCommand))]
public class InitDbCommand : IAsyncCommand
{
    private readonly CoreDbStructureBuilder _coreOrgBuilder;
    private readonly GetBaseAnnotationsReview _review;
    private readonly BaseAccountOptions _baseAccount;

    public InitDbCommand(
        CoreDbStructureBuilder coreOrgBuilder,
        GetBaseAnnotationsReview review,
        IOptions<BaseAccountOptions> baseAccount)
    {
        _coreOrgBuilder = coreOrgBuilder;
        _review = review;
        _baseAccount = baseAccount.Value;
    }

    [VersionOption("-v|--version", "", Description = "Display version of this tool.")]
    public bool IsVersionRequested { get; set; }

    public async Task<int> OnExecuteAsync()
    {
        // Checked before anything is written; an exception would end in the generic crash message of Program.
        if (!_baseAccount.HasValidAccountId())
        {
            Console.Write("! ", Color.Magenta);
            Console.WriteLine(
                $"{BaseAccountOptions.SECTION}:id '{_baseAccount.GetAccountId()}' is not an account id. Under Entra ID it is the owner's {{tid}}:{{oid}}, two lower-case GUIDs; under AuthKit or Keycloak the sub, without ':'.");
            return ExitCodes.ERROR_WRONG_INPUT;
        }

        try
        {
            var run = await _coreOrgBuilder.BuildAsync();
            DataSetOutput.WriteSummary(run);

            if (run.HasFailures)
            {
                return ExitCodes.Of(run);
            }
        }
        catch (DatabaseLayoutException exception)
        {
            DataSetOutput.WriteLayoutProblems(exception);
            return ExitCodes.ERROR_DATABASE_LAYOUT;
        }
        catch (DataSetException exception)
        {
            DataSetOutput.WriteWrongInput(exception);
            return ExitCodes.ERROR_WRONG_INPUT;
        }

        await _review.RenderReview();
        return ExitCodes.SUCCESS;
    }
}
