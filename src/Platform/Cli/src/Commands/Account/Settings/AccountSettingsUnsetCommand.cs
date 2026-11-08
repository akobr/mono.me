using System.Linq;
using System.Threading.Tasks;
using _42.CLI.Toolkit;
using _42.CLI.Toolkit.Output;
using _42.Platform.Cli.Configuration;
using McMaster.Extensions.CommandLineUtils;
using Newtonsoft.Json.Linq;

namespace _42.Platform.Cli.Commands.Account.Settings;

[Command(CommandNames.UNSET, Description = "Remove a preference, so sform uses its default again.")]
public class AccountSettingsUnsetCommand : BaseCommand
{
    private readonly IUserSettingsStore _settings;

    public AccountSettingsUnsetCommand(IExtendedConsole console, IUserSettingsStore settings)
        : base(console)
    {
        _settings = settings;
    }

    [Argument(0, "key", Description = "editor.type, editor.command, access.project or access.view.")]
    public string? Key { get; set; }

    public override Task<int> OnExecuteAsync()
    {
        return Task.FromResult(Execute());
    }

    private int Execute()
    {
        if (AccountSettingsSetCommand.FindDefinition(Console, Key) is not { } definition)
        {
            return ExitCodes.ERROR_WRONG_INPUT;
        }

        try
        {
            var current = _settings.Read()[definition.Section];

            // Also a value that the settings file has but sform cannot use, e.g. an editor type written by hand.
            var raw = UserSettingsSchema.GetValue(_settings.Load(), definition.Section) as JObject;
            var isSet = definition.Properties.Any(property =>
                UserSettingsSchema.GetValue(current.Values, property) is not null
                || (raw is not null && UserSettingsSchema.GetValue(raw, property) is not null));

            if (!isSet)
            {
                Console.WriteLine($"{definition.Key} is not set.");
                return ExitCodes.WARNING_NO_WORK_NEEDED;
            }

            // Moves the legacy sections first, so a value of an old file does not show through again.
            _settings.Update(definition.Section, definition.Properties.ToDictionary(property => property, _ => (JToken?)null));
        }
        catch (UserSettingsException exception)
        {
            Console.WriteImportant($"{definition.Key} could not be removed: {exception.Message}");
            return exception.ExitCode;
        }

        var consequence = definition.Key == UserSettingDefinitions.EDITOR_TYPE
            ? "; the next edit uses $VISUAL or $EDITOR, or asks."
            : definition.DefaultValue is { } defaultValue ? $"; sform uses {defaultValue}." : ".";
        Console.WriteLine($"{definition.Key} is unset{consequence}");
        return ExitCodes.SUCCESS;
    }
}
