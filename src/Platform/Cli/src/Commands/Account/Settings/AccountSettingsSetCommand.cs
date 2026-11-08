using System.Linq;
using System.Threading.Tasks;
using _42.CLI.Toolkit;
using _42.CLI.Toolkit.Output;
using _42.Platform.Cli.Configuration;
using McMaster.Extensions.CommandLineUtils;
using Newtonsoft.Json.Linq;

namespace _42.Platform.Cli.Commands.Account.Settings;

[Command(CommandNames.SET, Description = "Set a preference: editor.type, editor.command, access.project or access.view.")]
public class AccountSettingsSetCommand : BaseCommand
{
    private readonly IUserSettingsStore _settings;

    public AccountSettingsSetCommand(IExtendedConsole console, IUserSettingsStore settings)
        : base(console)
    {
        _settings = settings;
    }

    [Argument(0, "key", Description = "editor.type, editor.command, access.project or access.view.")]
    public string? Key { get; set; }

    [Argument(1, "value", Description = "The value; see sform account settings --help for what each key takes.")]
    public string? Value { get; set; }

    public override Task<int> OnExecuteAsync()
    {
        return Task.FromResult(Execute());
    }

    // Shared by set and unset.
    internal static UserSettingDefinition? FindDefinition(IExtendedConsole console, string? key)
    {
        if (UserSettingDefinitions.Find(key) is { } definition)
        {
            return definition;
        }

        console.WriteImportant(
            string.IsNullOrWhiteSpace(key) ? "Name the setting" : $"'{key}' is no setting sform knows",
            $"; the settings are {string.Join(", ", UserSettingDefinitions.All.Select(known => known.Key))}.");
        return null;
    }

    private int Execute()
    {
        if (FindDefinition(Console, Key) is not { } definition)
        {
            return ExitCodes.ERROR_WRONG_INPUT;
        }

        if (Value is null || definition.Parse(Value) is not { } changes)
        {
            Console.WriteImportant(
                Value is null ? $"Give the value of {definition.Key}" : $"'{Value}' is no value of {definition.Key}",
                definition.AllowedValues is { } allowed
                    ? $"; the values are {string.Join(", ", allowed)}."
                    : $"; it takes {definition.ValueFormat}.");
            return ExitCodes.ERROR_WRONG_INPUT;
        }

        try
        {
            var current = _settings.Read()[definition.Section];
            var after = UserSettingsSchema.Apply(definition.Section, current.Values, changes);
            var shown = definition.Read(after);

            if (current.Layer == UserSettingsLayer.Settings && JToken.DeepEquals(current.Values, after))
            {
                Console.WriteLine($"{definition.Key} is already ", (shown ?? Value).ThemedHighlight(Console.Theme), ".");
                return ExitCodes.WARNING_NO_WORK_NEEDED;
            }

            _settings.Update(definition.Section, changes);
            Console.WriteLine($"{definition.Key} = ", (shown ?? Value).ThemedHighlight(Console.Theme));

            // The command alone does nothing for another editor type, which is kept for switching back to custom.
            var editor = UserSettingsSchema.ToEditorOptions(after);

            if (definition.Key == UserSettingDefinitions.EDITOR_COMMAND && editor.EditorType != EditorType.Custom)
            {
                Console.WriteLine("sform uses it when editor.type is custom: sform account settings set editor.type custom.".ThemedLowlight(Console.Theme));
            }
            else if (definition.Key == UserSettingDefinitions.EDITOR_TYPE && editor is { EditorType: EditorType.Custom, CustomCommand: null })
            {
                Console.WriteLine("A custom editor needs its command too: sform account settings set editor.command \"code --wait\".".ThemedLowlight(Console.Theme));
            }

            return ExitCodes.SUCCESS;
        }
        catch (UserSettingsException exception)
        {
            Console.WriteImportant($"{definition.Key} could not be saved: {exception.Message}");
            return exception.ExitCode;
        }
    }
}
