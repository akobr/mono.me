using System;
using System.Collections.Generic;
using _42.Platform.Cli.Configuration;
using _42.Platform.Cli.Telemetry;
using Shouldly;
using Xunit;

namespace _42.Platform.Cli.UnitTests.Telemetry;

// When sform sends its usage event: the telemetry section of app.config.json and the opt-out variables.
public class TelemetrySettingsTests
{
    private const string Token = "phc_test-token";
    private const string Host = "https://eu.i.posthog.com";

    [Fact]
    public void ATokenAndAnHttpsHost_TurnTelemetryOn()
    {
        var settings = Resolve(new TelemetryOptions { PostHogProjectToken = $" {Token} ", PostHogHost = $"{Host} " });

        settings.IsEnabled.ShouldBeTrue();
        settings.ProjectToken.ShouldBe(Token);
        settings.Host.ShouldBe(new Uri(Host));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void ABlankToken_TurnsTelemetryOff(string? token)
    {
        var settings = Resolve(new TelemetryOptions { PostHogProjectToken = token, PostHogHost = Host });

        settings.IsEnabled.ShouldBeFalse();
        settings.ProjectToken.ShouldBeNull();
        settings.Host.ShouldBeNull();
    }

    // The SDK would send to its US cloud instead.
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("http://eu.i.posthog.com")]
    [InlineData("eu.i.posthog.com")]
    [InlineData("/batch")]
    [InlineData("ftp://eu.i.posthog.com")]
    public void AHostThatIsNotAnAbsoluteHttpsUrl_TurnsTelemetryOff(string? host)
    {
        var settings = Resolve(new TelemetryOptions { PostHogProjectToken = Token, PostHogHost = host });

        settings.IsEnabled.ShouldBeFalse();
    }

    [Theory]
    [InlineData(TelemetrySettings.DO_NOT_TRACK, "1")]
    [InlineData(TelemetrySettings.DO_NOT_TRACK, "true")]
    [InlineData(TelemetrySettings.DO_NOT_TRACK, "TRUE")]
    [InlineData(TelemetrySettings.DO_NOT_TRACK, "yes")]
    [InlineData(TelemetrySettings.DO_NOT_TRACK, " On ")]
    [InlineData(TelemetrySettings.SFORM_TELEMETRY_OPTOUT, "1")]
    [InlineData(TelemetrySettings.SFORM_TELEMETRY_OPTOUT, "True")]
    [InlineData(TelemetrySettings.SFORM_TELEMETRY_OPTOUT, "YES")]
    [InlineData(TelemetrySettings.SFORM_TELEMETRY_OPTOUT, "on")]
    public void AnOptOutVariable_TurnsTelemetryOff(string variable, string value)
    {
        var settings = Resolve(Configured(), new Dictionary<string, string> { [variable] = value });

        settings.IsEnabled.ShouldBeFalse();
        settings.ProjectToken.ShouldBeNull();
    }

    [Theory]
    [InlineData("0")]
    [InlineData("false")]
    [InlineData("no")]
    [InlineData("off")]
    [InlineData("")]
    [InlineData("2")]
    public void AnyOtherValue_DoesNotOptOut(string value)
    {
        var settings = Resolve(
            Configured(),
            new Dictionary<string, string>
            {
                [TelemetrySettings.DO_NOT_TRACK] = value,
                [TelemetrySettings.SFORM_TELEMETRY_OPTOUT] = value,
            });

        settings.IsEnabled.ShouldBeTrue();
    }

    [Theory]
    [InlineData("CI")]
    [InlineData("GITHUB_ACTIONS")]
    [InlineData("TF_BUILD")]
    [InlineData("GITLAB_CI")]
    [InlineData("JENKINS_URL")]
    public void ACiVariable_MarksTheRunAsContinuousIntegration(string variable)
    {
        Resolve(Configured(), new Dictionary<string, string> { [variable] = "true" }).IsContinuousIntegration.ShouldBeTrue();
        Resolve(Configured(), new Dictionary<string, string> { [variable] = " " }).IsContinuousIntegration.ShouldBeFalse();
    }

    private static TelemetryOptions Configured()
    {
        return new TelemetryOptions { PostHogProjectToken = Token, PostHogHost = Host };
    }

    private static TelemetrySettings Resolve(TelemetryOptions options, Dictionary<string, string>? environment = null)
    {
        return TelemetrySettings.Resolve(options, name => environment?.GetValueOrDefault(name));
    }
}
