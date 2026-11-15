using System;
using System.Collections.Generic;
using _42.Platform.Cli.Output;
using _42.Platform.Cli.UnitTests.Commands;
using _42.Platform.Storyteller.Sdk;
using Shouldly;
using Xunit;

namespace _42.Platform.Cli.UnitTests.Output;

// What Program.Main prints for a 401 or 403 that the command left to it.
public class ApiRefusalTests
{
    [Fact]
    public void Forbidden_WithTheServerMessage_PrintsItWithoutTheCredentialHint()
    {
        var refusal = ApiRefusal.Of(AnnotationCommandFixture.MissingScope("Annotation.Manage"));

        refusal.Message.ShouldBe("This operation needs the scope Annotation.Manage, which the machine was not created with.");
        refusal.ErrorCode.ShouldBe("AccessDenied");
        refusal.MayBeTheCredentials.ShouldBeFalse();
    }

    [Fact]
    public void Forbidden_WithAMessageAndNoCode_PrintsOnlyTheMessage()
    {
        var refusal = ApiRefusal.Of(AnnotationCommandFixture.Error(403, null, "No Administrator access to 'acme.billing'."));

        refusal.Message.ShouldBe("No Administrator access to 'acme.billing'.");
        refusal.ErrorCode.ShouldBeNull();
        refusal.MayBeTheCredentials.ShouldBeFalse();
    }

    [Fact]
    public void Forbidden_NotDeclaredByTheOperation_ReadsTheMessageFromTheResponseText()
    {
        var exception = new ApiException(
            "Forbidden",
            403,
            "{\"Message\":\"No Administrator access to 'acme.billing'.\",\"ErrorCode\":\"AccessDenied\"}",
            new Dictionary<string, IEnumerable<string>>(),
            null);

        var refusal = ApiRefusal.Of(exception);

        refusal.Message.ShouldBe("No Administrator access to 'acme.billing'.");
        refusal.ErrorCode.ShouldBe("AccessDenied");
        refusal.MayBeTheCredentials.ShouldBeFalse();
    }

    [Theory]
    [InlineData(403, null, null)]
    [InlineData(403, "AccessDenied", " ")]
    [InlineData(401, null, null)]
    [InlineData(401, "Unauthorized", "The token has expired.")]
    public void Unauthorized_OrForbiddenWithoutAMessage_PrintsTheGeneralSentenceWithTheCredentialHint(int statusCode, string? errorCode, string? message)
    {
        var refusal = ApiRefusal.Of(AnnotationCommandFixture.Error(statusCode, errorCode, message));

        refusal.Message.ShouldBe(ApiRefusal.GeneralMessage);
        refusal.ErrorCode.ShouldBeNull();
        refusal.MayBeTheCredentials.ShouldBeTrue();
        refusal.Hint.ShouldBeNull();
    }

    // 424-264: the step-up names the window and how to sign in again, never the machine hint.
    [Theory]
    [InlineData(true, true)]
    [InlineData(true, false)]
    [InlineData(false, true)]
    [InlineData(false, false)]
    public void StepUp_NamesTheWindowAndANewSignIn(bool typed, bool challenge)
    {
        var exception = AnnotationCommandFixture.StepUpRequired(typed, challenge);

        var refusal = ApiRefusal.Of(exception);

        // A typed exception keeps no text, so without the challenge the window is unknown: the server's message.
        refusal.Message.ShouldBe(typed && !challenge
            ? AnnotationCommandFixture.StepUpMessage
            : "This needs a recent sign-in: a sign-in counts for 10 minutes.");
        refusal.ErrorCode.ShouldBe("StepUpRequired");
        refusal.MayBeTheCredentials.ShouldBeFalse();
        var hint = refusal.Hint.ShouldNotBeNull();
        hint.ShouldBe(StepUpRefusal.Hint);
        hint.ShouldContain("sign out in the browser first");
        hint.ShouldContain("private window");
        hint.ShouldContain("'sform account logout' and 'sform account'");
        hint.ShouldContain("repeat the command");
    }

    [Fact]
    public void StepUp_ByTheChallengeAlone_WithoutABody_IsTheSentenceWithTheWindow()
    {
        var exception = new ApiException(
            "Unauthorized",
            401,
            string.Empty,
            new Dictionary<string, IEnumerable<string>> { ["WWW-Authenticate"] = ["Bearer error=\"insufficient_user_authentication\", max_age=3600"] },
            null);

        var refusal = ApiRefusal.Of(exception);

        refusal.Message.ShouldBe("This needs a recent sign-in: a sign-in counts for 1 hour.");
        refusal.ErrorCode.ShouldBe("StepUpRequired");
    }

    [Fact]
    public void StepUp_WithoutAWindowOrAMessage_IsTheSentenceAlone()
    {
        var exception = new ApiException(
            "Unauthorized",
            401,
            string.Empty,
            new Dictionary<string, IEnumerable<string>> { ["WWW-Authenticate"] = ["Bearer error=\"insufficient_user_authentication\""] },
            null);

        ApiRefusal.Of(exception).Message.ShouldBe(StepUpRefusal.GeneralMessage);
    }

    // The same code on a 403 is no step-up: the server's message, as for any 403.
    [Fact]
    public void StepUpCode_OnAForbidden_IsAnyOtherRefusal()
    {
        var refusal = ApiRefusal.Of(AnnotationCommandFixture.Error(403, "StepUpRequired", "No."));

        refusal.Message.ShouldBe("No.");
        refusal.Hint.ShouldBeNull();
    }

    [Theory]
    [InlineData(600, "10 minutes")]
    [InlineData(60, "1 minute")]
    [InlineData(3600, "1 hour")]
    [InlineData(7200, "2 hours")]
    [InlineData(90, "90 seconds")]
    [InlineData(1, "1 second")]
    public void StepUpWindow_IsWrittenOutAsTheApiWritesIt(int seconds, string expected)
    {
        StepUpRefusal.Describe(TimeSpan.FromSeconds(seconds)).ShouldBe(expected);
    }
}
