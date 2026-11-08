using System.Linq;
using System.Threading.Tasks;
using _42.CLI.Toolkit;
using _42.CLI.Toolkit.Output;
using _42.Platform.Cli.Authentication;
using _42.Platform.Cli.Configuration;
using McMaster.Extensions.CommandLineUtils;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace _42.Platform.Cli.Commands.Account.Settings;

[Subcommand(
    typeof(AccountSettingsEditorCommand),
    typeof(AccountSettingsSetCommand),
    typeof(AccountSettingsUnsetCommand))]
[Command(CommandNames.SETTINGS, Description = "Show and change your local preferences of sform.", ExtendedHelpText = HELP_TEXT)]
public class AccountSettingsCommand : BaseCommand
{
    // The keys of UserSettingDefinitions; a test keeps them in step.
    internal const string HELP_TEXT = """

        Settings:
          editor.type      vscode, neovim, vim or custom: the editor of the edit commands.
          editor.command   The command line of a custom editor, e.g. "code --wait"; the file path is appended.
          access.project   <organization>.<project>: the default project of the commands without -p.
          access.view      The default view of the commands without -v; default when not set.

        They are kept in sform.settings.json in your .42for.net folder (--path prints where), which survives tool updates.
        Without an editor, sform uses $VISUAL or $EDITOR, else asks on the first edit.

        Examples:
          sform account settings editor
          sform account settings editor custom --command "code --wait"
          sform account settings set access.project contoso.payments
          sform account settings unset editor.type
        """;

    private readonly IUserSettingsStore _settings;
    private readonly EnvironmentEditor _environment;
    private readonly MachineSignInSettings _target;

    public AccountSettingsCommand(IExtendedConsole console, IUserSettingsStore settings, EnvironmentEditor environment, MachineSignInSettings target)
        : base(console)
    {
        _settings = settings;
        _environment = environment;
        _target = target;
    }

    [Option("--path", CommandOptionType.NoValue, Description = "Print only the location of the settings file.")]
    public bool ShowPath { get; set; }

    [Option("--json", CommandOptionType.NoValue, Description = "Write the values that sform uses as JSON.")]
    public bool Json { get; set; }

    public override Task<int> OnExecuteAsync()
    {
        if (ShowPath)
        {
            WriteOut(_settings.FilePath);
            return Task.FromResult(ExitCodes.SUCCESS);
        }

        UserSettingsSnapshot snapshot;

        try
        {
            snapshot = _settings.Read();
        }
        catch (UserSettingsException exception)
        {
            Console.WriteImportant(exception.Message);
            return Task.FromResult(exception.ExitCode);
        }

        var values = UserSettingValue.Resolve(snapshot, _environment, _target);

        if (Json)
        {
            var json = new JObject(values.Select(value => new JProperty(value.Definition.Key, value.EffectiveValue)));
            WriteOut(json.ToString(Formatting.Indented));
            return Task.FromResult(ExitCodes.SUCCESS);
        }

        Console.WriteHeader("Settings");
        Console.WriteLine("File ", _settings.FilePath.ThemedHighlight(Console.Theme));
        Console.WriteTable(
            values,
            value => new[] { value.Definition.Key, value.Display, DescribeSource(value) },
            new[] { "Key", "Value", "Source" });
        Console.WriteLine("Change them with sform account settings editor, set and unset.".ThemedLowlight(Console.Theme));
        return Task.FromResult(ExitCodes.SUCCESS);
    }

    internal static string DescribeSource(UserSettingValue value)
    {
        var source = value.Source switch
        {
            UserSettingSource.Settings => "settings",
            UserSettingSource.Legacy => $"legacy {(value.Definition.Section == ConfigurationSections.EDITOR ? Constants.EDITOR_CONFIG_JSON : Constants.ACCESS_DEFAULT_JSON)}",
            UserSettingSource.Environment => value.Variable is EnvironmentEditor.VISUAL or EnvironmentEditor.EDITOR ? $"${value.Variable}" : value.Variable ?? "environment",
            UserSettingSource.Machine => "machine sign-in",
            _ => "default",
        };

        return value.Note is null ? source : $"{source} ({value.Note})";
    }

    // Plain text on the standard output, for scripts.
    private void WriteOut(string text)
    {
        (Console.Console?.Out ?? System.Console.Out).WriteLine(text);
    }
}
