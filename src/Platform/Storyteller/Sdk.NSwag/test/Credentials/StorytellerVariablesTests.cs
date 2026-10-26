using _42.Platform.Storyteller.Sdk.Credentials;

namespace _42.Platform.Storyteller.Sdk.UnitTests.Credentials;

public class StorytellerVariablesTests
{
    [Fact]
    public void Credentials_AreTheNamesThatMakeACredential_DeprecatedOnesIncluded()
    {
        StorytellerVariables.Credentials.ShouldBe(
        [
            "STORYTELLER_API_KEY",
            "STORYTELLER_CLIENT_CERTIFICATE",
            "STORYTELLER_CERTIFICATE_FILE",
            "STORYTELLER_CLIENT_CERTIFICATE_PASSWORD",
            "STORYTELLER_CERTIFICATE_PASSWORD",
            "STORYTELLER_CLIENT_ID",
            "STORYTELLER_CLIENT_SECRET",
            "STORYTELLER_TOKEN_URL",
            "STORYTELLER_TOKEN_SCOPE",
            "STORYTELLER_TOKEN_FILE",
            "STORYTELLER_SUBJECT_TOKEN_FILE",
            "STORYTELLER_SUBJECT_TOKEN_TYPE",
        ]);
    }

    [Theory]
    [InlineData(StorytellerVariables.ENDPOINT)]
    [InlineData(StorytellerVariables.API_URL)]
    [InlineData(StorytellerVariables.KEY)]
    [InlineData(StorytellerVariables.ACTIONS_ID_TOKEN_REQUEST_URL)]
    [InlineData(StorytellerVariables.ACTIONS_ID_TOKEN_REQUEST_TOKEN)]
    public void Credentials_LeaveOutTheTargetAndTheGitHubActionsNames(string name)
    {
        StorytellerVariables.Credentials.ShouldNotContain(name);
    }

    [Fact]
    public void Deprecated_MapsTheSformNamesToTheCanonicalOnes()
    {
        StorytellerVariables.Deprecated.ShouldBe(
            new Dictionary<string, string>
            {
                ["STORYTELLER_API_URL"] = "STORYTELLER_ENDPOINT",
                ["STORYTELLER_CERTIFICATE_FILE"] = "STORYTELLER_CLIENT_CERTIFICATE",
                ["STORYTELLER_CERTIFICATE_PASSWORD"] = "STORYTELLER_CLIENT_CERTIFICATE_PASSWORD",
                ["STORYTELLER_SUBJECT_TOKEN_FILE"] = "STORYTELLER_TOKEN_FILE",
            },
            ignoreOrder: true);
    }

    [Fact]
    public void TheReader_TrimsValues_AndTakesABlankValueAsUnset()
    {
        var reader = Reader(new() { ["STORYTELLER_CLIENT_ID"] = "  machine  ", ["STORYTELLER_TOKEN_SCOPE"] = " \t " });

        reader.Read("STORYTELLER_CLIENT_ID").ShouldBe("machine");
        reader.Read("STORYTELLER_TOKEN_SCOPE").ShouldBeNull();
        reader.Read("STORYTELLER_TOKEN_URL").ShouldBeNull();
        reader.SetNames.ShouldBe(["STORYTELLER_CLIENT_ID"]);
    }

    [Fact]
    public void TheReader_ReadsTheDeprecatedName_WhenTheCanonicalOneIsUnset()
    {
        var reader = Reader(new() { ["STORYTELLER_API_URL"] = "http://localhost:7071/api" });

        reader.ReadWithFallback("STORYTELLER_ENDPOINT", "STORYTELLER_API_URL", out var source).ShouldBe("http://localhost:7071/api");
        source.ShouldBe("STORYTELLER_API_URL");
    }

    [Fact]
    public void TheReader_AcceptsTheSameValueUnderBothNames_AndNamesTheCanonicalOne()
    {
        var reader = Reader(new() { ["STORYTELLER_ENDPOINT"] = "https://api.example.com/api", ["STORYTELLER_API_URL"] = " https://api.example.com/api " });

        reader.ReadWithFallback("STORYTELLER_ENDPOINT", "STORYTELLER_API_URL", out var source).ShouldBe("https://api.example.com/api");
        source.ShouldBe("STORYTELLER_ENDPOINT");
        reader.SetNames.ShouldBe(["STORYTELLER_ENDPOINT", "STORYTELLER_API_URL"]);
    }

    [Fact]
    public void TheReader_RefusesDifferentValuesUnderBothNames_AndNamesBothButNoValue()
    {
        var reader = Reader(new() { ["STORYTELLER_ENDPOINT"] = "https://one.example.com/api", ["STORYTELLER_API_URL"] = "https://two.example.com/api" });

        var exception = Should.Throw<StorytellerAuthenticationException>(() => reader.ReadWithFallback("STORYTELLER_ENDPOINT", "STORYTELLER_API_URL", out _));

        exception.Reason.ShouldBe(StorytellerAuthenticationFailureReason.Configuration);
        exception.Message.ShouldBe("STORYTELLER_ENDPOINT and STORYTELLER_API_URL are both set, to different values; set one of them.");
    }

    private static StorytellerVariableReader Reader(Dictionary<string, string?> variables)
    {
        return new StorytellerVariableReader(name => variables.GetValueOrDefault(name));
    }
}
