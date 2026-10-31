namespace _42.Testing.UI.Automation.Runtime.TUnit.UnitTests;

public sealed class TestParametersTests
{
    [Fact]
    public void ParseCommandLine_KeepsEverythingAfterTheFirstEquals()
    {
        var parameters = TestParameters.ParseCommandLine(["app.dll", "--test-parameter", "Target:BaseUrl=https://admin.example.test/?a=b", "Execution:Seed=42", "--report-trx"]);

        parameters.ShouldBe(
        [
            new KeyValuePair<string, string>("Target:BaseUrl", "https://admin.example.test/?a=b"),
            new KeyValuePair<string, string>("Execution:Seed", "42"),
        ]);
    }

    [Fact]
    public void ParseCommandLine_ReadsRepeatedAndInlineOptions()
    {
        var parameters = TestParameters.ParseCommandLine(["--test-parameter", "A=1", "--treenode-filter", "/**", "--test-parameter=B=2", "--test-parameter:C=x=y", "--test-parameter", "A=3"]);

        parameters.Select(pair => $"{pair.Key}={pair.Value}").ShouldBe(["A=1", "B=2", "C=x=y", "A=3"]);
    }

    [Fact]
    public void ParseCommandLine_IgnoresValuesWithoutAKey()
    {
        TestParameters.ParseCommandLine(["--test-parameter", "=1", "Flag", "--other", "X=1"]).ShouldBeEmpty();
    }

    [Fact]
    public void Merge_PrefersTheRawValueAndTheLastRepetition()
    {
        var parsed = new Dictionary<string, List<string>>
        {
            ["Target:BaseUrl"] = ["https://admin.example.test/?a"],
            ["Browser:Headless"] = ["true", "false"],
        };

        var merged = TestParameters.Merge(parsed, ["--test-parameter", "target:baseurl=https://admin.example.test/?a=b"]).ToDictionary(StringComparer.OrdinalIgnoreCase);

        merged["Target:BaseUrl"].ShouldBe("https://admin.example.test/?a=b");
        merged["Browser:Headless"].ShouldBe("false");
        merged.Count.ShouldBe(2);
    }
}
