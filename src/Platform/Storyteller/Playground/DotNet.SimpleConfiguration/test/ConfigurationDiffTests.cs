namespace _42.Platform.Storyteller.Playground.DotNet.SimpleConfiguration.UnitTests;

public class ConfigurationDiffTests
{
    [Fact]
    public void Compare_NamesAddedChangedAndRemovedKeys()
    {
        var changes = ConfigurationDiff.Compare(
            Keys(("same", "1"), ("changed", "1"), ("removed", "1")),
            Keys(("same", "1"), ("changed", "2"), ("added", "1")));

        changes.ShouldBe(
        [
            new KeyChange(KeyChangeKind.Changed, "changed", "1", "2"),
            new KeyChange(KeyChangeKind.Added, "added", null, "1"),
            new KeyChange(KeyChangeKind.Removed, "removed", "1", null),
        ]);
    }

    [Fact]
    public void Compare_KeysCompareWithoutCase_ValuesOrdinally()
    {
        var changes = ConfigurationDiff.Compare(Keys(("Key", "value")), Keys(("KEY", "VALUE")));

        changes.ShouldBe([new KeyChange(KeyChangeKind.Changed, "KEY", "value", "VALUE")]);
    }

    [Fact]
    public void Compare_KeysThatOnlyLookNumericallyEqual_AreTwoKeys()
    {
        var changes = ConfigurationDiff.Compare(Keys(("a:1", "x")), Keys(("a:1", "x"), ("a:01", "y")));

        changes.ShouldBe([new KeyChange(KeyChangeKind.Added, "a:01", null, "y")]);
    }

    private static Dictionary<string, string> Keys(params (string Key, string Value)[] keys)
    {
        return keys.ToDictionary(pair => pair.Key, pair => pair.Value, StringComparer.OrdinalIgnoreCase);
    }
}
