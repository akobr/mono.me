using System.Threading.Tasks;
using _42.CLI.Toolkit;
using _42.CLI.Toolkit.Output;
using _42.Platform.Cli.Configuration;
using _42.Platform.Cli.Services;
using McMaster.Extensions.CommandLineUtils;
using Newtonsoft.Json.Linq;

namespace _42.Platform.Cli.Commands.Account.Settings;

[Command(CommandNames.EDITOR, Description = "Choose the editor that sform opens documents in; without a type it asks.")]
public class AccountSettingsEditorCommand : BaseCommand
{
    private readonly IEditorService _editor;
    private readonly IUserSettingsStore _settings;

    public AccountSettingsEditorCommand(IExtendedConsole console, IEditorService editor, IUserSettingsStore settings)
        : base(console)
    {
        _editor = editor;
        _settings = settings;
    }

    [Argument(0, "type", Description = "vscode, neovim, vim or custom.")]
    public string? Type { get; set; }

    [Option("-c|--command", CommandOptionType.SingleValue, Description = "The command line of a custom editor, e.g. \"code --wait\"; the file path is appended.")]
    public string? EditorCommand { get; set; }

    public override Task<int> OnExecuteAsync()
    {
        return Task.FromResult(Execute());
    }

    internal static string Describe(EditorOptions options)
    {
        var name = options.EditorType is { } type ? UserSettingDefinitions.ToEditorName(type) : "none";
        return options.EditorType == EditorType.Custom ? $"{name} ({options.CustomCommand})" : name;
    }

    private int Execute()
    {
        EditorOptions options;

        if (Type is null && EditorCommand is null)
        {
            if (Console.Console?.IsInputRedirected == true)
            {
                Console.WriteLine("Name the editor: sform account settings editor <vscode|neovim|vim|custom> [--command \"<command>\"].");
                return ExitCodes.ERROR_WRONG_INPUT;
            }

            options = _editor.PromptEditorPreference(Console);

            if (!options.IsConfigured)
            {
                Console.WriteImportant("No editor command was entered, nothing was saved.");
                return ExitCodes.WARNING_ABORTED;
            }
        }
        else if (TryGetOptions(out var given))
        {
            options = given;
        }
        else
        {
            return ExitCodes.ERROR_WRONG_INPUT;
        }

        var changes = UserSettingsSchema.EditorChanges(options);

        try
        {
            var current = _settings.Read()[ConfigurationSections.EDITOR];

            if (current.Layer == UserSettingsLayer.Settings
                && JToken.DeepEquals(current.Values, UserSettingsSchema.Apply(ConfigurationSections.EDITOR, new JObject(), changes)))
            {
                Console.WriteLine("The editor is already ", Describe(options).ThemedHighlight(Console.Theme), ".");
                return ExitCodes.WARNING_NO_WORK_NEEDED;
            }

            _settings.Update(ConfigurationSections.EDITOR, changes);
        }
        catch (UserSettingsException exception)
        {
            Console.WriteImportant($"The editor could not be saved: {exception.Message}");
            return exception.ExitCode;
        }

        Console.WriteLine("Editor set to ", Describe(options).ThemedHighlight(Console.Theme), ".");

        if (!_editor.IsEditorAvailable(options))
        {
            Console.WriteImportant($"{Describe(options)} was not found on this machine; the choice is saved anyway.");
        }

        return ExitCodes.SUCCESS;
    }

    private bool TryGetOptions(out EditorOptions options)
    {
        options = new EditorOptions();
        EditorType type;

        if (Type is null)
        {
            // --command alone is a custom editor.
            type = EditorType.Custom;
        }
        else if (UserSettingDefinitions.ParseEditorName(Type) is { } parsed)
        {
            type = parsed;
        }
        else
        {
            Console.WriteImportant($"'{Type}' is no editor sform knows; use {string.Join(", ", UserSettingDefinitions.EditorTypeNames)}.");
            return false;
        }

        if (type == EditorType.Custom && string.IsNullOrWhiteSpace(EditorCommand))
        {
            Console.WriteImportant("A custom editor needs its command line: sform account settings editor custom --command \"code --wait\".");
            return false;
        }

        if (type != EditorType.Custom && EditorCommand is not null)
        {
            Console.WriteImportant($"--command is for a custom editor only; {UserSettingDefinitions.ToEditorName(type)} needs none.");
            return false;
        }

        options = new EditorOptions { EditorType = type, CustomCommand = type == EditorType.Custom ? EditorCommand!.Trim() : null };
        return true;
    }
}
