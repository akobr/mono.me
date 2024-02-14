using System.IO.Abstractions;
using System.Linq;
using System.Net;
using System.Threading.Tasks;
using _42.CLI.Toolkit;
using _42.CLI.Toolkit.Output;
using _42.Platform.Cli.Configuration;
using _42.Platform.Sdk.Api;
using McMaster.Extensions.CommandLineUtils;
using Microsoft.Extensions.Options;
using Newtonsoft.Json;
using Sharprompt;

namespace _42.Platform.Cli.Commands.Account;

[Command(CommandNames.SET, Description = "Set default project as a context of the CLI toolkit.")]
public class AccountSetCommand : BaseCommand
{
    private readonly IAccessApiAsync _accessApi;
    private readonly IFileSystem _fileSystem;
    private readonly AccessDefaultOptions _accessDefault;

    public AccountSetCommand(
        IExtendedConsole console,
        IAccessApiAsync accessApi,
        IOptions<AccessDefaultOptions> accessDefaultOptions,
        IFileSystem fileSystem)
        : base(console)
    {
        _accessApi = accessApi;
        _fileSystem = fileSystem;
        _accessDefault = accessDefaultOptions.Value;
    }

    [Argument(0, Description = "A project key to set.")]
    public string? ProjectKey { get; } = string.Empty;

    public override async Task<int> OnExecuteAsync()
    {
        var accountResponse = await _accessApi.GetAccountWithHttpInfoAsync();

        if (accountResponse.StatusCode is not HttpStatusCode.OK)
        {
            Console.Write(
                "You account is not registered, to create a registration call ",
                "sform account register ".ThemedHighlight(Console.Theme),
                "command.");
            return ExitCodes.INTERACTION_NEEDED;
        }

        var account = accountResponse.Data;

        if (!string.IsNullOrWhiteSpace(_accessDefault.ProjectKey))
        {
            Console.WriteLine("Currently the default project is set to ", _accessDefault.ProjectKey.ThemedHighlight(Console.Theme), ".");
        }

        var projectKey = ProjectKey ?? SelectPossibleProject(account);
        _accessDefault.ProjectKey = projectKey;
        _accessDefault.OrganizationKey = projectKey[..projectKey.IndexOf('.')];

        CreateAccessDefaultConfigFile(_accessDefault, _fileSystem);

        Console.WriteLine("Default project set to ", projectKey.ThemedHighlight(Console.Theme), ".");
        return ExitCodes.SUCCESS;
    }

    public static void CreateAccessDefaultConfigFile(AccessDefaultOptions options, IFileSystem fileSystem)
    {
        var config = new { access = options };
        var serializer = JsonSerializer.Create(new JsonSerializerSettings { Formatting = Formatting.Indented });
        using var fileWriter = fileSystem.File.CreateText(Constants.ACCESS_DEFAULT_JSON);
        var jWriter = new JsonTextWriter(fileWriter);
        serializer.Serialize(jWriter, config);
    }

    private string SelectPossibleProject(Sdk.Model.Account account)
    {
        var projectKey = Console.Select(new SelectOptions<string>
        {
            Message = "Which project would you like to set as default",
            DefaultValue = _accessDefault.ProjectKey,
            Items = account.AccessMap
                .Where(access => access.Key.Contains('.'))
                .Select(access => access.Key)
                .OrderBy(access => access),
        });
        return projectKey;
    }
}
