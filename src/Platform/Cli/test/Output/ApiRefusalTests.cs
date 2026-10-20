using _42.Platform.Cli.Output;
using _42.Platform.Cli.UnitTests.Commands;
using Shouldly;
using Xunit;

namespace _42.Platform.Cli.UnitTests.Output;

// What Program.Main prints for a 401 or 403 that the command left to it.
public class ApiRefusalTests
{
    [Fact]
    public void Forbidden_WithTheServerMessage_PrintsItWithoutTheCredentialHint()
    {
        var refusal = ApiRefusal.Of(AnnotationCommandFixture.MissingManagementScope("Annotation.Manage"));

        refusal.Message.ShouldBe("This operation needs the management scope Annotation.Manage, which the machine was not created with.");
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
    }
}
