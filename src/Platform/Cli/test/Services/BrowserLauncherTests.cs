using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.IO.Abstractions.TestingHelpers;
using System.Linq;
using _42.Platform.Cli.Configuration;
using _42.Platform.Cli.Services;
using Shouldly;
using Xunit;

namespace _42.Platform.Cli.UnitTests.Services;

// sform account --browser: the program of SFORM_BROWSER with the address as its only argument; else the entries of BROWSER
// as other tools read them, then the default browser; else the default browser through the shell.
public class BrowserLauncherTests
{
    private static readonly Uri Address = new("https://auth.example.com/device?user_code=BCDF-GHJK&x=1");

    private readonly Dictionary<string, string> _environment = new();
    private readonly List<ProcessStartInfo> _started = [];

    [Fact]
    public void WithoutAVariable_TheShellOpensTheAddress()
    {
        var startInfo = Create().CreateStartInfos(Address).ShouldHaveSingleItem();

        ShouldBeTheShellDefault(startInfo);
    }

    // SFORM_BROWSER is the program alone, spaces and all, and nothing else is tried.
    [Fact]
    public void SformBrowser_ItsProgramGetsTheAddressAsItsOnlyArgument_WithoutAShell()
    {
        _environment[SformVariables.BROWSER] = "/opt/tools/record url %s";

        var startInfo = Create().CreateStartInfos(Address).ShouldHaveSingleItem();

        startInfo.UseShellExecute.ShouldBeFalse();
        startInfo.FileName.ShouldBe("/opt/tools/record url %s");
        startInfo.ArgumentList.ShouldBe([Address.AbsoluteUri]);
    }

    [Fact]
    public void Browser_AProgramGetsTheAddress_ThenTheDefaultBrowserIsTried()
    {
        _environment[SformVariables.CONVENTIONAL_BROWSER] = "firefox";

        var startInfos = Create().CreateStartInfos(Address);

        startInfos.Count.ShouldBe(2);
        startInfos[0].UseShellExecute.ShouldBeFalse();
        startInfos[0].FileName.ShouldBe("firefox");
        startInfos[0].ArgumentList.ShouldBe([Address.AbsoluteUri]);
        ShouldBeTheShellDefault(startInfos[1]);
    }

    // The list of BROWSER: separated by the path separator, an entry with %s is a command line.
    [Fact]
    public void Browser_AListWithACommandLine_IsTriedEntryByEntry()
    {
        _environment[SformVariables.CONVENTIONAL_BROWSER] = string.Join(Path.PathSeparator, "firefox", " ", "xdg-open --new-window '%s'", "\"/opt/my browser/run\" --url=%s");

        var startInfos = Create().CreateStartInfos(Address);

        startInfos.Select(startInfo => startInfo.FileName).ShouldBe(["firefox", "xdg-open", "/opt/my browser/run", Address.AbsoluteUri]);
        startInfos[1].ArgumentList.ShouldBe(["--new-window", Address.AbsoluteUri]);
        startInfos[2].ArgumentList.ShouldBe([$"--url={Address.AbsoluteUri}"]);
        ShouldBeTheShellDefault(startInfos[3]);
    }

    [Fact]
    public void SformBrowser_WinsOverBrowser()
    {
        _environment[SformVariables.BROWSER] = "record-url";
        _environment[SformVariables.CONVENTIONAL_BROWSER] = "firefox";

        Create().CreateStartInfos(Address).ShouldHaveSingleItem().FileName.ShouldBe("record-url");
    }

    [Fact]
    public void TryOpen_StartsTheProgram()
    {
        _environment[SformVariables.BROWSER] = "record-url";
        var launcher = Create(Record);

        launcher.TryOpen(Address).ShouldBeTrue();

        _started.ShouldHaveSingleItem().ArgumentList.ShouldBe([Address.AbsoluteUri]);
    }

    [Fact]
    public void TryOpen_OfAnSformBrowserThatCannotStart_IsFalse()
    {
        _environment[SformVariables.BROWSER] = "record-url";

        Create(CannotStart).TryOpen(Address).ShouldBeFalse();
    }

    // A BROWSER entry that cannot start, e.g. one set for another tool: the next entry, then the default browser.
    [Fact]
    public void TryOpen_OfABrowserEntryThatCannotStart_GoesOnToTheDefaultBrowser()
    {
        _environment[SformVariables.CONVENTIONAL_BROWSER] = "no-such-browser";
        var launcher = Create(startInfo => startInfo.UseShellExecute ? Record(startInfo) : CannotStart(startInfo));

        launcher.TryOpen(Address).ShouldBeTrue();

        ShouldBeTheShellDefault(_started.ShouldHaveSingleItem());
    }

    // The real process start of a program that does not exist.
    [Fact]
    public void TryOpen_OfAMissingProgram_IsFalse()
    {
        _environment[SformVariables.BROWSER] = Path.Combine(Path.GetTempPath(), "sform-no-such-browser-" + Guid.NewGuid().ToString("N"));

        new BrowserLauncher(Environment()).TryOpen(Address).ShouldBeFalse();
    }

    private static IDisposable? CannotStart(ProcessStartInfo startInfo)
    {
        throw new Win32Exception(2, "The system cannot find the file specified.");
    }

    private static void ShouldBeTheShellDefault(ProcessStartInfo startInfo)
    {
        startInfo.UseShellExecute.ShouldBeTrue();
        startInfo.FileName.ShouldBe(Address.AbsoluteUri);
        startInfo.ArgumentList.ShouldBeEmpty();
    }

    private IDisposable? Record(ProcessStartInfo startInfo)
    {
        _started.Add(startInfo);
        return null;
    }

    private BrowserLauncher Create(Func<ProcessStartInfo, IDisposable?>? start = null)
    {
        return new BrowserLauncher(Environment(), start ?? (_ => throw new InvalidOperationException("Not started in this test.")));
    }

    private SformEnvironment Environment()
    {
        var paths = CliPaths.Create(new MockFileSystem(), null, MockUnixSupport.Path(@"C:\tools\sform"), () => MockUnixSupport.Path(@"C:\Users\ada"));
        return SformEnvironment.FromEnvironment(name => _environment.GetValueOrDefault(name), paths);
    }
}
