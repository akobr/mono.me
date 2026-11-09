using System.Collections.Generic;
using System.IO.Abstractions.TestingHelpers;
using _42.Platform.Cli.Configuration;
using Shouldly;
using Xunit;

using static System.IO.Abstractions.TestingHelpers.MockUnixSupport;

namespace _42.Platform.Cli.UnitTests.Configuration;

// The SFORM_* variables, read from an injected environment as MachineSignInSettings reads the STORYTELLER_* ones.
public class SformEnvironmentTests
{
    private static readonly string Home = Path(@"C:\state\ada");

    private readonly Dictionary<string, string> _environment = new();

    [Fact]
    public void Nothing_IsTheDefaultBehaviour()
    {
        var environment = Read(home: null);

        environment.IsFileTokenStore.ShouldBeFalse();
        environment.TokenStoreNotice.ShouldBeNull();
        environment.IsDeviceCodeJson.ShouldBeFalse();
        environment.BrowserCommand.ShouldBeNull();
        environment.BrowserVariable.ShouldBeNull();
    }

    [Theory]
    [InlineData("file")]
    [InlineData("FILE")]
    [InlineData(" File ")]
    public void FileTokenStore_WithHome_IsHonouredAndAnnounced(string value)
    {
        _environment[SformVariables.TOKEN_STORE] = value;

        var environment = Read(Home);

        environment.IsFileTokenStore.ShouldBeTrue();
        environment.TokenStoreNotice.ShouldBe($"The session is kept unencrypted in {Home} (SFORM_TOKEN_STORE=file); keep the folder private.");
    }

    [Fact]
    public void FileTokenStore_WithoutHome_IsIgnoredWithAWarning()
    {
        _environment[SformVariables.TOKEN_STORE] = "file";

        var environment = Read(home: null);

        environment.IsFileTokenStore.ShouldBeFalse();
        environment.TokenStoreNotice.ShouldBe("SFORM_TOKEN_STORE=file is ignored without SFORM_HOME; the session is kept in the protected store.");
    }

    [Fact]
    public void UnknownTokenStore_IsIgnoredWithAWarning()
    {
        _environment[SformVariables.TOKEN_STORE] = "plain";

        var environment = Read(Home);

        environment.IsFileTokenStore.ShouldBeFalse();
        environment.TokenStoreNotice.ShouldBe("SFORM_TOKEN_STORE=plain is ignored: the only value is file, together with SFORM_HOME.");
    }

    [Theory]
    [InlineData("json", true)]
    [InlineData("Json", true)]
    [InlineData(" JSON ", true)]
    [InlineData("text", false)]
    [InlineData("", false)]
    public void DeviceCodeOutput_IsJsonOnlyForJson(string value, bool expected)
    {
        _environment[SformVariables.DEVICE_CODE_OUTPUT] = value;

        Read(Home).IsDeviceCodeJson.ShouldBe(expected);
    }

    [Fact]
    public void SformBrowser_WinsOverBrowser()
    {
        _environment[SformVariables.BROWSER] = "/opt/record-url";
        _environment[SformVariables.CONVENTIONAL_BROWSER] = "firefox";

        var environment = Read(Home);

        environment.BrowserCommand.ShouldBe("/opt/record-url");
        environment.BrowserVariable.ShouldBe("SFORM_BROWSER");
    }

    [Theory]
    [InlineData(null)]
    [InlineData("  ")]
    public void Browser_IsUsedWithoutSformBrowser(string? sformBrowser)
    {
        if (sformBrowser is not null)
        {
            _environment[SformVariables.BROWSER] = sformBrowser;
        }

        _environment[SformVariables.CONVENTIONAL_BROWSER] = "firefox";

        var environment = Read(Home);

        environment.BrowserCommand.ShouldBe("firefox");
        environment.BrowserVariable.ShouldBe("BROWSER");
    }

    private SformEnvironment Read(string? home)
    {
        var paths = CliPaths.Create(new MockFileSystem(), home, Path(@"C:\tools\sform"), () => Path(@"C:\Users\ada"));
        return SformEnvironment.FromEnvironment(name => _environment.GetValueOrDefault(name), paths);
    }
}
