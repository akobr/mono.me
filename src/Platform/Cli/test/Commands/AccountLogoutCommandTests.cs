using System.Collections.Generic;
using System.IO.Abstractions.TestingHelpers;
using System.Threading;
using System.Threading.Tasks;
using _42.Platform.Cli.Authentication;
using _42.Platform.Cli.Commands.Account;
using _42.Platform.Cli.UnitTests.Authentication;
using _42.Platform.Storyteller.Sdk;
using _42.Platform.Storyteller.Sdk.Credentials;
using Moq;
using Shouldly;
using Xunit;

namespace _42.Platform.Cli.UnitTests.Commands;

public class AccountLogoutCommandTests
{
    private readonly RecordingConsole _console = new();
    private readonly Mock<IAccessApiClient> _accessApi = new(MockBehavior.Strict);
    private readonly Mock<IAuthenticationService> _authentication = new(MockBehavior.Strict);
    private readonly Dictionary<string, string> _environment = [];

    [Fact]
    public async Task Machine_HasNothingToLogOutAndKeepsTheUserSession()
    {
        _environment[StorytellerVariables.ENDPOINT] = "https://storyteller.example.com/api";
        _environment[StorytellerVariables.API_KEY] = TestApiKeys.Create("acme", "billing", "3f2c");

        var exitCode = await Execute();

        exitCode.ShouldBe(ExitCodes.SUCCESS);
        _console.Lines.ShouldBe(["! sform uses the machine credentials in STORYTELLER_API_KEY. Nothing is stored, so there is nothing to log out; unset the variables to sign in as a user."]);
    }

    [Fact]
    public async Task User_WithoutASession_StillClearsWhatIsStored()
    {
        _authentication.Setup(authentication => authentication.GetSignedInUserAsync(It.IsAny<CancellationToken>())).ReturnsAsync((SignedInUser?)null);
        _authentication.Setup(authentication => authentication.LogoutAsync(It.IsAny<CancellationToken>())).Returns(Task.CompletedTask);

        var exitCode = await Execute();

        exitCode.ShouldBe(ExitCodes.SUCCESS);
        _authentication.Verify(authentication => authentication.LogoutAsync(It.IsAny<CancellationToken>()), Times.Once);
        _console.Lines.ShouldBe(["! No account is logged in this CLI instance."]);
    }

    private Task<int> Execute()
    {
        var machine = TestMachines.Create(_environment, new MockFileSystem(), new ScriptedHttpHandler());

        return new AccountLogoutCommand(_console.Object, _accessApi.Object, _authentication.Object, machine).OnExecuteAsync();
    }
}
