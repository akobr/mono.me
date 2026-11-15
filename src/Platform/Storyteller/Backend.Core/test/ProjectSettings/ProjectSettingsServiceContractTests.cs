using _42.Platform.Storyteller.Accessing;

namespace _42.Platform.Storyteller.Backend.Core.UnitTests;

// The conditions and the errors of IProjectSettingsService: what the API answers with 412 and 400.
public class ProjectSettingsServiceContractTests
{
    [Theory]
    [InlineData(0)]
    [InlineData(7)]
    public void Condition_NoneAndAnything_HoldForEveryVersion(long version)
    {
        ProjectSettingsCondition.None.Holds(version).ShouldBeTrue();
        ProjectSettingsCondition.Anything.Holds(version).ShouldBeTrue();
    }

    [Fact]
    public void Condition_OfVersions_HoldsOnlyForThem()
    {
        var condition = ProjectSettingsCondition.Of([3, 7, 7]);

        condition.Versions.ShouldBe([3, 7]);
        condition.Holds(3).ShouldBeTrue();
        condition.Holds(7).ShouldBeTrue();
        condition.Holds(0).ShouldBeFalse();
        ProjectSettingsCondition.Version(0).Holds(0).ShouldBeTrue();
        ProjectSettingsCondition.Version(0).Holds(1).ShouldBeFalse();
    }

    // An If-Match of only tags that are no version: a list that names nothing, which no version meets.
    [Fact]
    public void Condition_OfNoVersions_NeverHolds()
    {
        ProjectSettingsCondition.Of([]).Holds(0).ShouldBeFalse();
    }

    [Fact]
    public void InvalidSettings_IsAnInvalidInputWithEveryProblem()
    {
        ProjectSettingsProblem[] problems =
        [
            new("$.Types.sbt.Color", ProjectSettingsRules.Color, "'beige' is not a color of the catalog."),
            new("$.DateFormat", ProjectSettingsRules.DateFormat, "'yy' is not a date format."),
        ];

        InvalidInputException exception = new ProjectSettingsInvalidException(problems);

        exception.ErrorCode.ShouldBe(ErrorCodes.InvalidSettings);
        exception.Message.ShouldBe("The project settings have 2 problems; the first is $.Types.sbt.Color: 'beige' is not a color of the catalog.");
        ((ProjectSettingsInvalidException)exception).Problems.ShouldBe(problems);
        new ProjectSettingsInvalidException([problems[1]]).Message.ShouldBe("The project settings are not valid: $.DateFormat: 'yy' is not a date format.");
    }

    [Fact]
    public void PreconditionFailed_NamesTheCurrentVersion()
    {
        PreconditionFailedException exception = new ProjectSettingsPreconditionFailedException(7);

        exception.ErrorCode.ShouldBe(ErrorCodes.SettingsPreconditionFailed);
        exception.Message.ShouldBe("The project settings changed meanwhile; they are at version 7.");
        ((ProjectSettingsPreconditionFailedException)exception).CurrentVersion.ShouldBe(7);
    }
}
