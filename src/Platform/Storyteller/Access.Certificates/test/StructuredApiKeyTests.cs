using System.Text;

namespace _42.Platform.Storyteller.Access.Certificates.UnitTests;

// The structured API key: a malformed key is no key, so the caller gets a 401 before any lookup in Cosmos DB.
public class StructuredApiKeyTests
{
    [Fact]
    public void Format_ThenTryParse_RoundTrips()
    {
        var key = new StructuredApiKey("acme", "billing", "machine-1", "secret");

        StructuredApiKey.TryParse(key.Format()).ShouldBe(key);
    }

    [Theory]
    [InlineData(" :billing:machine-1")]
    [InlineData("acme: :machine-1")]
    [InlineData("acme:billing:\t")]
    [InlineData(":billing:machine-1")]
    public void TryParse_ABlankSegment_IsNoKey(string metadata)
    {
        var encoded = Convert.ToBase64String(Encoding.UTF8.GetBytes(metadata)).Replace('+', '-').Replace('/', '_').TrimEnd('=');

        StructuredApiKey.TryParse($"2s.{encoded}.secret").ShouldBeNull();
    }
}
