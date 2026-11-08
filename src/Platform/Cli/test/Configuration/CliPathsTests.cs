using System;
using System.IO.Abstractions.TestingHelpers;
using _42.Platform.Cli.Configuration;
using _42.Platform.Cli.Output.Exceptions;
using Shouldly;
using Xunit;

using static System.IO.Abstractions.TestingHelpers.MockUnixSupport;

namespace _42.Platform.Cli.UnitTests.Configuration;

// Where sform keeps its files: next to the assembly and in the user's .42for.net folder, or all of it in SFORM_HOME.
public class CliPathsTests
{
    private static readonly string ApplicationDirectory = Path(@"C:\tools\sform\");
    private static readonly string UserRoot = Path(@"C:\Users\ada\AppData\Local");
    private static readonly string Home = Path(@"C:\state\ada");

    private readonly MockFileSystem _fileSystem = new();

    [Fact]
    public void WithoutHome_TheConfigurationFilesAreNextToTheAssembly_AndTheSessionInTheUserFolder()
    {
        var paths = Create(null);

        paths.IsHomeSet.ShouldBeFalse();
        paths.Home.ShouldBeNull();
        paths.ApplicationDirectory.ShouldBe(Path(@"C:\tools\sform"));
        paths.StateDirectory.ShouldBe(Path(@"C:\tools\sform"));
        paths.AccessDefaultFile.ShouldBe(Path(@"C:\tools\sform\access.default.json"));
        paths.EditorConfigFile.ShouldBe(Path(@"C:\tools\sform\editor.config.json"));
        paths.AuthDiscoveryFile.ShouldBe(Path(@"C:\tools\sform\auth.discovery.json"));
        paths.GetLogFilePath(null).ShouldBe(Path(@"C:\tools\sform\logs\cli.log"));
        paths.UserDirectory.ShouldBe(Path(@"C:\Users\ada\AppData\Local\.42for.net"));
    }

    [Fact]
    public void WithHome_EveryFileIsInIt_AndTheUserProfileIsNeverRead()
    {
        var paths = CliPaths.Create(_fileSystem, Home, ApplicationDirectory, () => throw new InvalidOperationException("The user profile was read."));

        paths.IsHomeSet.ShouldBeTrue();
        paths.Home.ShouldBe(Home);
        paths.ApplicationDirectory.ShouldBe(Path(@"C:\tools\sform"));
        paths.StateDirectory.ShouldBe(Home);
        paths.UserDirectory.ShouldBe(Home);
        paths.AccessDefaultFile.ShouldBe(Path(@"C:\state\ada\access.default.json"));
        paths.EditorConfigFile.ShouldBe(Path(@"C:\state\ada\editor.config.json"));
        paths.AuthDiscoveryFile.ShouldBe(Path(@"C:\state\ada\auth.discovery.json"));
        paths.GetLogFilePath("logs/cli.log").ShouldBe(Path(@"C:\state\ada\logs\cli.log"));
    }

    // The spaces go around the converted path: MockUnixSupport.Path strips the drive only at the start of the text.
    [Theory]
    [InlineData(@"C:\state\ada\", false)]
    [InlineData(@"C:\state\ada", true)]
    [InlineData(@"C:\state\other\..\ada", false)]
    public void Home_IsNormalised(string home, bool padded)
    {
        var value = Path(home);

        Create(padded ? $"  {value}  " : value).Home.ShouldBe(Home);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void BlankHome_IsNotSet(string home)
    {
        Create(home).IsHomeSet.ShouldBeFalse();
    }

    [Theory]
    [InlineData("state")]
    [InlineData("state/ada")]
    [InlineData("./state")]
    public void RelativeHome_IsAWrongInput(string home)
    {
        var exception = Should.Throw<WrongInputException>(() => Create(home));

        exception.ExitCode.ShouldBe(ExitCodes.ERROR_WRONG_INPUT);
        exception.Message.ShouldBe($"SFORM_HOME must be an absolute path, and '{home}' is relative.");
    }

    [Fact]
    public void HomeThatIsAFile_IsAWrongInput()
    {
        _fileSystem.AddFile(Home, new MockFileData("not a folder"));

        Should.Throw<WrongInputException>(() => Create(Home)).Message.ShouldBe($"SFORM_HOME must be a directory, and '{Home}' is a file.");
    }

    // The directory is created by the first write, not when sform starts.
    [Fact]
    public void MissingHome_IsAcceptedAndCreatedBeforeAWrite()
    {
        var paths = Create(Home);

        _fileSystem.Directory.Exists(Home).ShouldBeFalse();

        paths.EnsureDirectoryOf(paths.AccessDefaultFile);

        _fileSystem.Directory.Exists(Home).ShouldBeTrue();
    }

    [Fact]
    public void AbsoluteLogFilePath_IsKept()
    {
        Create(Home).GetLogFilePath(Path(@"D:\logs\sform.log")).ShouldBe(Path(@"D:\logs\sform.log"));
    }

    [Fact]
    public void DailyLogFile_HasTheDayInItsName()
    {
        Create(Home).GetDailyLogFilePath(null, new DateTime(2026, 11, 8)).ShouldBe(Path(@"C:\state\ada\logs\cli20261108.log"));
    }

    [Fact]
    public void FromEnvironment_ReadsSformHome()
    {
        var paths = CliPaths.FromEnvironment(_fileSystem, name => name == SformVariables.HOME ? Home : null);

        paths.Home.ShouldBe(Home);
        paths.ApplicationDirectory.ShouldBe(_fileSystem.Path.TrimEndingDirectorySeparator(AppContext.BaseDirectory));
    }

    private CliPaths Create(string? home)
    {
        return CliPaths.Create(_fileSystem, home, ApplicationDirectory, () => UserRoot);
    }
}
