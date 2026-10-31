namespace _42.Platform.Storyteller.Api.Functions.UnitTests;

// The process settings Program.cs applies first. The schema check bounds its own pattern matches, so this default
// covers NJsonSchema's format validators, the match NJsonSchema repeats, and the caller's name queries.
public class ProcessDefaultsTests
{
    [Fact]
    public void Apply_SetsTheDefaultRegexMatchTimeout()
    {
        ProcessDefaults.Apply();

        AppContext.GetData("REGEX_DEFAULT_MATCH_TIMEOUT").ShouldBe(TimeSpan.FromMilliseconds(200));
        ProcessDefaults.RegexMatchTimeout.ShouldBe(TimeSpan.FromMilliseconds(200));
    }
}
