using System.Collections.Generic;
using System.IO.Abstractions.TestingHelpers;
using System.Threading;
using System.Threading.Tasks;
using _42.Platform.Cli.Authentication;
using _42.Platform.Cli.Commands.AccessPoints;
using _42.Platform.Cli.Commands.Account;
using _42.Platform.Cli.Configuration;
using _42.Platform.Cli.UnitTests.Configuration;
using _42.Platform.Storyteller.Sdk;
using Microsoft.Extensions.Options;
using Moq;
using Newtonsoft.Json.Linq;
using Sharprompt;
using Shouldly;
using Xunit;

namespace _42.Platform.Cli.UnitTests.Commands;

// account set, account register and access-point create keep the default project in ~/.42for.net/sform.settings.json,
// which survives tool updates, and never next to the assembly again.
public class AccountDefaultProjectWritersTests
{
    private static readonly string SettingsPath = UserSettingsStoreTests.SettingsPath;

    private readonly MockFileSystem _fileSystem = new(new Dictionary<string, MockFileData>(), MockUnixSupport.Path(@"C:\work"));
    private readonly RecordingConsole _console = new();
    private readonly Mock<IAccessApiClient> _accessApi = new(MockBehavior.Strict);
    private readonly Mock<IAuthenticationService> _authentication = new(MockBehavior.Strict);

    [Fact]
    public async Task Set_SavesTheProjectAndTheView_InTheSettingsFile()
    {
        AnAccount();

        var exitCode = await Set(new AccessDefaultOptions(), "contoso.payments", "preview");

        exitCode.ShouldBe(ExitCodes.SUCCESS);
        Stored().ShouldBeJson("""{"access":{"organizationName":"contoso","projectName":"payments","viewName":"preview"}}""");
        _console.Lines.ShouldBe(["Default project set to contoso.payments with view preview."]);
        _fileSystem.File.Exists(UserSettingsStoreTests.LegacyAccessPath).ShouldBeFalse();
    }

    [Fact]
    public async Task Set_KeepsTheStoredView_AndMovesTheLegacyFiles()
    {
        AnAccount();
        _fileSystem.AddFile(UserSettingsStoreTests.LegacyAccessPath, new MockFileData("""{"access":{"OrganizationName":"house","ProjectName":"main","ViewName":"staging"}}"""));
        _fileSystem.AddFile(UserSettingsStoreTests.LegacyEditorPath, new MockFileData(UserSettingsStoreTests.LegacyEditorJson));

        var exitCode = await Set(new AccessDefaultOptions { OrganizationName = "house", ProjectName = "main", ViewName = "staging" }, "acme.web", null);

        exitCode.ShouldBe(ExitCodes.SUCCESS);
        Stored().ShouldBeJson("""
            {
              "access": { "organizationName": "acme", "projectName": "web", "viewName": "staging" },
              "editor": { "editorType": "Neovim" }
            }
            """);
        _fileSystem.File.Exists(UserSettingsStoreTests.LegacyAccessPath).ShouldBeFalse();
        _fileSystem.File.Exists(UserSettingsStoreTests.LegacyEditorPath).ShouldBeFalse();
    }

    // Before, a key without a dot crashed the split; it is refused before any call.
    [Theory]
    [InlineData("payments")]
    [InlineData(".payments")]
    [InlineData("contoso.")]
    public async Task Set_RefusesAProjectKeyWithoutBothParts(string projectKey)
    {
        var exitCode = await Set(new AccessDefaultOptions(), projectKey, null);

        exitCode.ShouldBe(ExitCodes.ERROR_WRONG_INPUT);
        _console.Lines.ShouldBe([$"! '{projectKey}' is no project key; it takes <organization>.<project>."]);
        _accessApi.VerifyNoOtherCalls();
    }

    [Theory]
    [InlineData(true, ExitCodes.ERROR_WRONG_INPUT)]
    [InlineData(false, ExitCodes.ERROR_CRASH)]
    public async Task Set_ThatCannotSave_FailsWithTheReason(bool isDamaged, int expected)
    {
        AnAccount();
        var store = new Mock<IUserSettingsStore>();
        store.Setup(settings => settings.Update(It.IsAny<string>(), It.IsAny<IReadOnlyDictionary<string, JToken?>>()))
            .Throws(new UserSettingsException("The settings file x cannot be written: Access denied.", isDamaged));

        var exitCode = await new AccountSetCommand(_console.Object, _accessApi.Object, Options.Create(new AccessDefaultOptions()), store.Object)
        {
            ProjectKey = "contoso.payments",
        }.OnExecuteAsync();

        exitCode.ShouldBe(expected);
        _console.Lines.ShouldBe(["! The default project could not be saved: The settings file x cannot be written: Access denied."]);
    }

    [Fact]
    public async Task Register_SavesTheNewProject_AsTheDefault()
    {
        NotRegistered();
        _authentication.Setup(authentication => authentication.GetSignedInUserAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new SignedInUser("user_01", "ada@example.com", "Ada"));
        _console.Mock.SetupSequence(console => console.Input(It.IsAny<InputOptions<string>>())).Returns("contoso").Returns("payments");
        _accessApi.Setup(api => api.CreateAccountAsync(It.IsAny<AccountCreate>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new _42.Platform.Storyteller.Sdk.Account { Id = "1", Name = "Ada", UserName = "ada@example.com" });

        var exitCode = await new AccountRegisterCommand(_console.Object, _accessApi.Object, _authentication.Object, CreateStore()).OnExecuteAsync();

        exitCode.ShouldBe(ExitCodes.SUCCESS);
        Stored().ShouldBeJson("""{"access":{"organizationName":"contoso","projectName":"payments","viewName":"default"}}""");
    }

    // The account exists by then: a default project that cannot be saved is a warning, not a failure.
    [Fact]
    public async Task Register_ThatCannotSave_WarnsAndSucceeds()
    {
        _fileSystem.AddFile(SettingsPath, new MockFileData("{"));
        NotRegistered();
        _authentication.Setup(authentication => authentication.GetSignedInUserAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new SignedInUser("user_01", "ada@example.com", "Ada"));
        _console.Mock.SetupSequence(console => console.Input(It.IsAny<InputOptions<string>>())).Returns("contoso").Returns("payments");
        _accessApi.Setup(api => api.CreateAccountAsync(It.IsAny<AccountCreate>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new _42.Platform.Storyteller.Sdk.Account { Id = "1", Name = "Ada", UserName = "ada@example.com" });

        var exitCode = await new AccountRegisterCommand(_console.Object, _accessApi.Object, _authentication.Object, CreateStore()).OnExecuteAsync();

        exitCode.ShouldBe(ExitCodes.SUCCESS);
        _console.Lines.ShouldContain(line => line.StartsWith($"! The default project could not be saved: The settings file {SettingsPath} is damaged: "));
        _fileSystem.File.ReadAllText(SettingsPath).ShouldBe("{");
    }

    [Fact]
    public async Task AccessPointCreate_SavesTheNewProject_AsTheDefault()
    {
        AnAccount();
        _console.Mock.Setup(console => console.Select(It.IsAny<SelectOptions<string>>())).Returns("contoso");
        _console.Mock.Setup(console => console.Input(It.IsAny<InputOptions<string>>())).Returns("billing");
        _accessApi.Setup(api => api.CreateAccessPointAsync(It.IsAny<AccessPointCreate>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new AccessPoint());

        var exitCode = await new AccessPointCreateCommand(_console.Object, _accessApi.Object, CreateStore()).OnExecuteAsync();

        exitCode.ShouldBe(ExitCodes.SUCCESS);
        Stored().ShouldBeJson("""{"access":{"organizationName":"contoso","projectName":"billing","viewName":"default"}}""");
    }

    [Fact]
    public async Task AccessPointCreate_ThatCannotSave_WarnsAndSucceeds()
    {
        AnAccount();
        _console.Mock.Setup(console => console.Select(It.IsAny<SelectOptions<string>>())).Returns("contoso");
        _console.Mock.Setup(console => console.Input(It.IsAny<InputOptions<string>>())).Returns("billing");
        _accessApi.Setup(api => api.CreateAccessPointAsync(It.IsAny<AccessPointCreate>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new AccessPoint());
        var store = new Mock<IUserSettingsStore>();
        store.Setup(settings => settings.Update(It.IsAny<string>(), It.IsAny<IReadOnlyDictionary<string, JToken?>>()))
            .Throws(new UserSettingsException("The settings file x cannot be written: Access denied.", isDamaged: false));

        var exitCode = await new AccessPointCreateCommand(_console.Object, _accessApi.Object, store.Object).OnExecuteAsync();

        exitCode.ShouldBe(ExitCodes.SUCCESS);
        _console.Lines.ShouldBe(["! The default project could not be saved: The settings file x cannot be written: Access denied."]);
    }

    private void AnAccount()
    {
        _accessApi.Setup(api => api.GetAccountAsync(It.IsAny<CancellationToken>())).ReturnsAsync(new _42.Platform.Storyteller.Sdk.Account
        {
            Id = "1",
            Name = "Ada",
            UserName = "ada@example.com",
            AccessMap = new Dictionary<string, AccountRole> { ["contoso"] = AccountRole.Owner, ["contoso.payments"] = AccountRole.Owner },
        });
    }

    private void NotRegistered()
    {
        _accessApi.Setup(api => api.GetAccountAsync(It.IsAny<CancellationToken>()))
            .ThrowsAsync(new ApiException("HTTP 404", 404, string.Empty, new Dictionary<string, IEnumerable<string>>(), null));
    }

    private Task<int> Set(AccessDefaultOptions stored, string? projectKey, string? viewName)
    {
        return new AccountSetCommand(_console.Object, _accessApi.Object, Options.Create(stored), CreateStore())
        {
            ProjectKey = projectKey,
            ViewName = viewName,
        }.OnExecuteAsync();
    }

    private UserSettingsStore CreateStore()
    {
        return new UserSettingsStore(_fileSystem, SettingsPath, UserSettingsStoreTests.LegacyPaths());
    }

    private JObject Stored()
    {
        return JObject.Parse(_fileSystem.File.ReadAllText(SettingsPath));
    }
}
