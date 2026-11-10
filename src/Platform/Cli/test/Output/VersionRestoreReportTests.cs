using _42.Platform.Cli.Output;
using _42.Platform.Storyteller.Sdk;
using Shouldly;
using Xunit;

namespace _42.Platform.Cli.UnitTests.Output;

// The line of config, template and schema … restore (424-221) and its exit code.
public class VersionRestoreReportTests
{
    private const string Subject = "the configuration for 'rst.billing'";

    [Fact]
    public void Restored_NamesTheNewVersion()
    {
        var report = VersionRestoreReport.Create(Subject, Result(VersionRestoreResultOutcome.Restored, 1, 4));

        report.Message.ShouldBe("Restored v1 of the configuration for 'rst.billing' as v4.");
        report.ExitCode.ShouldBe(ExitCodes.SUCCESS);
    }

    [Fact]
    public void UnchangedCurrentVersion_IsAlreadyCurrent()
    {
        var report = VersionRestoreReport.Create(Subject, Result(VersionRestoreResultOutcome.Unchanged, 4, 4));

        report.Message.ShouldBe("v4 is already the current version of the configuration for 'rst.billing'.");
        report.ExitCode.ShouldBe(ExitCodes.WARNING_NO_WORK_NEEDED);
    }

    [Fact]
    public void UnchangedOtherVersion_EqualsTheCurrentVersion()
    {
        var report = VersionRestoreReport.Create("the schema for 'unt' under 'rst.billing'", Result(VersionRestoreResultOutcome.Unchanged, 2, 7));

        report.Message.ShouldBe("v2 equals the current version v7 of the schema for 'unt' under 'rst.billing'; nothing changed.");
        report.ExitCode.ShouldBe(ExitCodes.WARNING_NO_WORK_NEEDED);
    }

    [Fact]
    public void LargeVersions_AreWrittenWithoutSeparators()
    {
        var report = VersionRestoreReport.Create("the template for 'unt'", Result(VersionRestoreResultOutcome.Restored, 1200, 12345));

        report.Message.ShouldBe("Restored v1200 of the template for 'unt' as v12345.");
    }

    private static VersionRestoreResult Result(VersionRestoreResultOutcome outcome, long fromVersion, long version)
    {
        return new VersionRestoreResult { Outcome = outcome, FromVersion = fromVersion, Version = version };
    }
}
