using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Shouldly;

namespace _42.Platform.Cli.UnitTests.Configuration;

internal static class JsonAssertions
{
    // The same JSON, whatever the order of the properties and the formatting.
    public static void ShouldBeJson(this JToken? actual, string expected)
    {
        var expectedToken = JToken.Parse(expected);
        JToken.DeepEquals(actual, expectedToken).ShouldBeTrue(
            $"expected {expectedToken.ToString(Formatting.None)} but was {actual?.ToString(Formatting.None) ?? "null"}");
    }
}
