namespace _42.Platform.Storyteller.AspireHosting.UnitTests;

// Shouldly compares two dictionaries entry by entry in order, and the order of the variables follows the order of the
// writes and of the callbacks, which is not the contract.
internal static class VariableAssertions
{
    // Exactly these variables, in any order.
    public static void ShouldBeVariables(this IReadOnlyDictionary<string, string?> actual, IReadOnlyDictionary<string, string?> expected)
    {
        actual.ShouldBe(expected, ignoreOrder: true);
    }
}
