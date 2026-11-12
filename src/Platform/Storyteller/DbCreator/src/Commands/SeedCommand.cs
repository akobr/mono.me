using System.Diagnostics;
using System.Threading.Tasks;
using _42.CLI.Toolkit;
using _42.Platform.Storyteller.DbCreator.DataSets;
using _42.Platform.Storyteller.DbCreator.Logic;
using McMaster.Extensions.CommandLineUtils;

namespace _42.Platform.Storyteller.DbCreator.Commands;

[Command(Name, Description = "Seed data sets into an organization, project and view, created when missing. A set seeded already in its version and with its files is skipped; otherwise what exists stays and what is missing is created.")]
public class SeedCommand : IAsyncCommand
{
    public const string Name = "seed";

    private readonly CoreDbStructureBuilder _structure;
    private readonly DataSetSeeder _seeder;

    public SeedCommand(CoreDbStructureBuilder structure, DataSetSeeder seeder)
    {
        _structure = structure;
        _seeder = seeder;
    }

    [Argument(0, "sets", Description = "The sets to seed, built-in or of the --path folders; what they depend on is seeded first. Without names, the sets of the --path folders.")]
    public string[]? Sets { get; set; }

    [Option("--path", CommandOptionType.MultipleValue, ValueName = "folder", Description = "A folder with set.json, or a folder of such folders; its sets join the built-in ones. Repeatable.")]
    public string[]? Paths { get; set; }

    [Option("--organization", CommandOptionType.SingleValue, ValueName = "organization", Description = "The organization to seed into, created with the owner when missing. Default: the Organization of the named sets' set.json.")]
    public string? Organization { get; set; }

    [Option("--project", CommandOptionType.SingleValue, ValueName = "project", Description = "The project to seed into, created when missing; an existing one is used as it is. Default: the Project of the named sets' set.json.")]
    public string? Project { get; set; }

    [Option("--view", CommandOptionType.SingleValue, ValueName = "view", Description = "The view to seed into, registered when missing. Default: the View of the named sets' set.json, otherwise default.")]
    public string? View { get; set; }

    [Option("--owner", CommandOptionType.SingleValue, ValueName = "account-id", Description = "The account that owns a created organization and project: under Entra ID its {tid}:{oid}, under AuthKit or Keycloak its sub. Default: baseAccount:id.")]
    public string? Owner { get; set; }

    [Option("--dry-run", CommandOptionType.NoValue, Description = "Decide every outcome and write nothing of the target and the sets (the containers are ensured).")]
    public bool IsDryRun { get; set; }

    [Option("--force", CommandOptionType.NoValue, Description = "Store schemas and configurations that break each other, with a warning.")]
    public bool IsForced { get; set; }

    [Option("--again", CommandOptionType.NoValue, Description = "Seed a set that is seeded already in its version and with its files.")]
    public bool IsAgain { get; set; }

    public DataSetSeedRequest CreateRequest()
    {
        return new DataSetSeedRequest
        {
            Sets = Sets ?? [],
            Paths = Paths ?? [],
            Organization = Organization,
            Project = Project,
            View = View,
            Owner = Owner,
            Options = new DataSetSeedOptions
            {
                DryRun = IsDryRun,
                Force = IsForced,
                Again = IsAgain,
            },
        };
    }

    public async Task<int> OnExecuteAsync()
    {
        var started = Stopwatch.GetTimestamp();
        var request = CreateRequest();
        var options = request.Options with { AnnotationProgress = DataSetOutput.AnnotationProgress };

        try
        {
            // the files and the options first, so wrong input never waits for the database
            var plan = _seeder.Plan(request);
            DataSetOutput.WritePlan(plan, options);

            await _structure.EnsureContainersAsync();
            var run = await _seeder.SeedAsync(plan, options);

            DataSetOutput.WriteRun(run, Stopwatch.GetElapsedTime(started));
            return ExitCodes.Of(run);
        }
        catch (DataSetException exception)
        {
            DataSetOutput.WriteWrongInput(exception);
            return ExitCodes.ERROR_WRONG_INPUT;
        }
        catch (DatabaseLayoutException exception)
        {
            DataSetOutput.WriteLayoutProblems(exception);
            return ExitCodes.ERROR_DATABASE_LAYOUT;
        }
    }
}
