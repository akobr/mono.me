using System.Collections.Generic;
using System.IO.Abstractions.TestingHelpers;
using System.Threading.Tasks;
using _42.Platform.Cli.Authentication;
using _42.Platform.Cli.Commands;
using _42.Platform.Cli.Configuration;
using _42.Platform.Cli.UnitTests.Authentication;
using _42.Platform.Storyteller.Sdk;
using _42.Platform.Storyteller.Sdk.Credentials;
using Microsoft.Extensions.Options;
using Moq;
using Shouldly;
using Xunit;

namespace _42.Platform.Cli.UnitTests.Commands;

public class CaDownloadCommandTests
{
    [Fact]
    public async Task Machine_WithoutAProject_DownloadsTheAnonymousCertificateAuthority()
    {
        var environment = new Dictionary<string, string>
        {
            [StorytellerVariables.ENDPOINT] = "https://storyteller.example.com/api",
            [StorytellerVariables.TOKEN_FILE] = "/ci/token",
        };
        var machine = TestMachines.Create(environment, new MockFileSystem(), new ScriptedHttpHandler());
        var accessApi = new Mock<IAccessApiClient>();
        accessApi.Setup(api => api.GetCertificateAuthorityAsync()).ReturnsAsync("-----BEGIN CERTIFICATE-----");
        var console = new RecordingConsole();
        var command = new CaDownloadCommand(console.Object, new CommandContext(Options.Create(new AccessDefaultOptions()), machine), accessApi.Object, new MockFileSystem());

        var exitCode = await command.OnExecuteAsync();

        exitCode.ShouldBe(ExitCodes.SUCCESS);
        console.Lines.ShouldContain("-----BEGIN CERTIFICATE-----");
    }
}
