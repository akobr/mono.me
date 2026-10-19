using System;
using _42.Platform.Cli.Telemetry;
using McMaster.Extensions.CommandLineUtils;
using Shouldly;
using Xunit;

namespace _42.Platform.Cli.UnitTests.Telemetry;

// The outcome property: the first matching row of the table wins.
public class CommandOutcomeTests
{
    [Fact]
    public void Cancelled_WinsOverEverythingElse()
    {
        var run = new CommandRun
        {
            Cancelled = true,
            Information = CommandOutcome.HELP,
            Exception = new InvalidOperationException(),
            Crashed = true,
        };

        CommandOutcome.Of(run, ExitCodes.ERROR_CRASH).ShouldBe(CommandOutcome.CANCELLED);
    }

    [Theory]
    [InlineData(CommandOutcome.HELP)]
    [InlineData(CommandOutcome.VERSION)]
    public void ShownInformation_IsItsOwnOutcome(string information)
    {
        CommandOutcome.Of(new CommandRun { Information = information }, ExitCodes.SUCCESS).ShouldBe(information);
    }

    // A missing option value falls into the last catch of Main, which also marks a crash.
    [Fact]
    public void ACommandParsingException_IsAnInputError()
    {
        var run = new CommandRun
        {
            Exception = new CommandParsingException(new CommandLineApplication(), "Missing value for option 'r'"),
            Crashed = true,
        };

        CommandOutcome.Of(run, ExitCodes.ERROR_CRASH).ShouldBe(CommandOutcome.INPUT_ERROR);
    }

    [Fact]
    public void ANegativeExitCode_IsAnInputError()
    {
        CommandOutcome.Of(new CommandRun(), ExitCodes.ERROR_INPUT_PARSING).ShouldBe(CommandOutcome.INPUT_ERROR);
    }

    [Fact]
    public void AnExceptionInTheLastCatch_IsACrash()
    {
        var run = new CommandRun { Exception = new InvalidOperationException(), Crashed = true };

        CommandOutcome.Of(run, ExitCodes.ERROR_CRASH).ShouldBe(CommandOutcome.CRASH);
    }

    [Fact]
    public void ExitCodeZero_IsASuccess()
    {
        CommandOutcome.Of(new CommandRun(), ExitCodes.SUCCESS).ShouldBe(CommandOutcome.SUCCESS);
    }

    [Theory]
    [InlineData(ExitCodes.WARNING_ABORTED)]
    [InlineData(ExitCodes.WARNING_NO_WORK_NEEDED)]
    [InlineData(ExitCodes.WARNING_UNAUTHORIZED_ACCESS)]
    [InlineData(ExitCodes.WARNING_PARTIAL_RESULT)]
    [InlineData(99)]
    public void ExitCodesFrom40_AreWarnings(int exitCode)
    {
        CommandOutcome.Of(new CommandRun(), exitCode).ShouldBe(CommandOutcome.WARNING);
    }

    // Exit code 1 without a crash is a validation failure of CommandLineUtils, e.g. a missing required option.
    [Theory]
    [InlineData(ExitCodes.ERROR_CRASH)]
    [InlineData(ExitCodes.ERROR_WRONG_INPUT)]
    [InlineData(ExitCodes.ERROR_SERVICE_FAILURE)]
    [InlineData(39)]
    public void OtherExitCodes_AreErrors(int exitCode)
    {
        CommandOutcome.Of(new CommandRun(), exitCode).ShouldBe(CommandOutcome.ERROR);
    }
}
