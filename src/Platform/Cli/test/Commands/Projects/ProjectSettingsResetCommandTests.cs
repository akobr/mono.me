using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using _42.Platform.Cli.Commands.Projects;
using Moq;
using Newtonsoft.Json.Linq;
using Sharprompt;
using Shouldly;
using Xunit;

namespace _42.Platform.Cli.UnitTests.Commands.Projects;

// sform project settings reset: a merge patch of nulls for every part or the named ones, after a confirmation, under the
// version that was read.
public class ProjectSettingsResetCommandTests
{
    private readonly ProjectSettingsFixture _fixture = new();

    [Fact]
    public async Task Reset_WithYes_RemovesEveryPart_UnderTheVersionRead()
    {
        _fixture.SetupRead(ProjectSettingsFixture.Changed());
        var sent = _fixture.SetupPatch(ProjectSettingsFixture.Defaults(version: 4));

        var exitCode = await Command(yes: true).OnExecuteAsync();

        exitCode.ShouldBe(ExitCodes.SUCCESS);
        var (patch, ifMatch) = sent.ShouldHaveSingleItem();
        JToken.DeepEquals(patch, JToken.Parse("""{"Types":null,"DefaultTimeZone":null,"DateFormat":null}""")).ShouldBeTrue(patch.ToString());
        ifMatch.ShouldBe("\"3\"");
        _fixture.Lines.ShouldBe(["# Changes", "Saved the project settings, version 4."]);
        _fixture.VerifyRemembered(4);
    }

    [Theory]
    [InlineData(true, false, false, """{"Types":null}""")]
    [InlineData(false, true, false, """{"DefaultTimeZone":null}""")]
    [InlineData(false, false, true, """{"DateFormat":null}""")]
    [InlineData(false, true, true, """{"DefaultTimeZone":null,"DateFormat":null}""")]
    public async Task Reset_OfParts_RemovesOnlyThem(bool types, bool timeZone, bool dateFormat, string expected)
    {
        _fixture.SetupRead(ProjectSettingsFixture.Changed());
        var sent = _fixture.SetupPatch(ProjectSettingsFixture.Changed(version: 4));

        var exitCode = await Command(yes: true, types: types, timeZone: timeZone, dateFormat: dateFormat).OnExecuteAsync();

        exitCode.ShouldBe(ExitCodes.SUCCESS);
        var patch = sent.ShouldHaveSingleItem().Patch;
        JToken.DeepEquals(patch, JToken.Parse(expected)).ShouldBeTrue(patch.ToString());
    }

    [Fact]
    public async Task Reset_OfSettingsAtTheDefaults_WritesNothing()
    {
        var settings = ProjectSettingsFixture.Changed();
        settings.DefaultTimeZone = null;
        _fixture.SetupRead(settings);

        var exitCode = await Command(timeZone: true).OnExecuteAsync();

        exitCode.ShouldBe(ExitCodes.WARNING_NO_WORK_NEEDED);
        _fixture.Lines.ShouldBe(["The project settings did not change; they stay at version 3."]);
        _fixture.VerifyNothingRemembered();
    }

    [Fact]
    public async Task Reset_Confirmed_IsWritten()
    {
        _fixture.SetupRead(ProjectSettingsFixture.Changed());
        var sent = _fixture.SetupPatch(ProjectSettingsFixture.Changed(version: 4));
        _fixture.Console.Mock
            .Setup(console => console.Confirm(It.Is<ConfirmOptions>(options => options.Message == "Reset the types of org1.proj1 to the defaults" && options.DefaultValue == false)))
            .Returns(true);

        var exitCode = await Command(types: true).OnExecuteAsync();

        exitCode.ShouldBe(ExitCodes.SUCCESS);
        sent.ShouldHaveSingleItem();

        // the changes come before the question, and not again
        _fixture.Lines.ShouldBe(["# Changes", "Saved the project settings, version 4."]);
    }

    [Fact]
    public async Task Reset_Declined_WritesNothing()
    {
        _fixture.SetupRead(ProjectSettingsFixture.Changed());
        _fixture.Console.Mock.Setup(console => console.Confirm(It.IsAny<ConfirmOptions>())).Returns(false);

        var exitCode = await Command().OnExecuteAsync();

        exitCode.ShouldBe(ExitCodes.WARNING_ABORTED);
        _fixture.Lines[^1].ShouldBe("Nothing was reset.");
        _fixture.Console.Mock.Verify(console => console.Confirm(It.Is<ConfirmOptions>(options => options.Message == "Reset the types, the default time zone and the date format of org1.proj1 to the defaults")));
    }

    [Fact]
    public async Task Reset_WithoutYes_OnRedirectedInput_NeedsInteraction()
    {
        _fixture.Console.UseInnerConsole(redirectedInput: true);
        _fixture.SetupRead(ProjectSettingsFixture.Changed());

        var exitCode = await Command().OnExecuteAsync();

        exitCode.ShouldBe(ExitCodes.WARNING_INTERACTION_NEEDED);
        _fixture.Console.ErrorLines.ShouldBe([
            "! sform cannot ask for the confirmation of the reset: the input is redirected.",
            "  Pass -y|--yes to reset them without asking.",
        ]);
    }

    [Fact]
    public async Task Reset_OfSettingsThatChangedMeanwhile_IsRefused()
    {
        _fixture.SetupRead(ProjectSettingsFixture.Changed());
        _fixture.SetupPatch(ProjectSettingsFixture.PreconditionFailed(5));

        var exitCode = await Command(yes: true).OnExecuteAsync();

        exitCode.ShouldBe(ExitCodes.ERROR_WRONG_INPUT);
        _fixture.Lines.ShouldBe(["! The project settings changed meanwhile; they are at version 5."]);
    }

    [Fact]
    public async Task Reset_Refused_PrintsTheServerMessage()
    {
        _fixture.SetupRead(ProjectSettingsFixture.Changed());
        _fixture.SetupPatch(ProjectSettingsFixture.Forbidden());

        var exitCode = await Command(yes: true).OnExecuteAsync();

        exitCode.ShouldBe(ExitCodes.WARNING_UNAUTHORIZED_ACCESS);
        _fixture.Lines.ShouldBe(["! Changing the project settings needs a user with the Administrator role.", "Error code: AccessDenied"]);
    }

    private ProjectSettingsResetCommand Command(bool yes = false, bool types = false, bool timeZone = false, bool dateFormat = false)
    {
        return new ProjectSettingsResetCommand(_fixture.Console.Object, _fixture.Context.Object, _fixture.Access.Object, _fixture.Cache.Object)
        {
            Yes = yes,
            IsTypesReset = types,
            IsTimeZoneReset = timeZone,
            IsDateFormatReset = dateFormat,
        };
    }
}
