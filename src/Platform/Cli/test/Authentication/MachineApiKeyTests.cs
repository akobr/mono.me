using _42.Platform.Cli.Authentication;
using Shouldly;
using Xunit;

namespace _42.Platform.Cli.UnitTests.Authentication;

public class MachineApiKeyTests
{
    [Fact]
    public void TryParse_StructuredKey_NamesTheMachineButKeepsNoSecret()
    {
        var key = MachineApiKey.TryParse(TestApiKeys.Create("acme", "billing", "3f2c", "top-secret"));

        key.ShouldBe(new MachineApiKey("acme", "billing", "3f2c"));
        key!.ToString().ShouldNotContain("top-secret");
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("not-a-key")]
    [InlineData("2s.YWNtZTpiaWxsaW5nOjNmMmM")]
    [InlineData("3s.YWNtZTpiaWxsaW5nOjNmMmM.secret")]
    [InlineData("2s.!!!.secret")]
    [InlineData("2s.YWNtZTpiaWxsaW5n.secret")]
    [InlineData("2s..secret")]
    public void TryParse_OtherFormats_GiveNoIdentity(string? key)
    {
        // YWNtZTpiaWxsaW5nOjNmMmM is acme:billing:3f2c, YWNtZTpiaWxsaW5n is acme:billing.
        MachineApiKey.TryParse(key).ShouldBeNull();
    }

    // The machine segment as the SDK's StorytellerCredentialVariables.ApiKeyMachine gives it, without the secret.
    [Theory]
    [InlineData("YWNtZTpiaWxsaW5nOjNmMmM", true)]
    [InlineData(null, false)]
    [InlineData("", false)]
    [InlineData("!!!", false)]
    [InlineData("YWNtZTpiaWxsaW5n", false)]
    public void TryParseMachine_ReadsTheSegmentAlone(string? segment, bool isStructured)
    {
        MachineApiKey.TryParseMachine(segment).ShouldBe(isStructured ? new MachineApiKey("acme", "billing", "3f2c") : null);
    }
}
