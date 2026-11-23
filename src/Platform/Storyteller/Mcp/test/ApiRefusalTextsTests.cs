using System.Text.RegularExpressions;

using _42.Platform.Storyteller.Mcp.Authentication;

namespace _42.Platform.Storyteller.Mcp.UnitTests;

// The MCP server tells a missing account and a blocked client app from other refusals of the token exchange by the
// API's exact error_description (StorytellerTokenExchangeProvider.ReadAnswer). The server does not reference the API's
// assemblies, so this test reads the API's constants from its source file: a change of the API's wording fails here
// instead of turning the server's log into the misleading hint about SubjectAudiences.
public class ApiRefusalTextsTests
{
    private const string SourcePath = "src/Platform/Storyteller/Backend.Core/src/Accessing/Federation/UserTokenExchangeResult.cs";

    [Theory]
    [InlineData("NoAccountDescription", StorytellerSignInRefusedException.NoAccountMessage)]
    [InlineData("ClientBlockedDescription", StorytellerSignInRefusedException.ClientBlockedMessage)]
    public void TheServersTexts_AreTheApisDescriptions(string constant, string serverText)
    {
        var source = File.ReadAllText(Path.Combine(FindRepositoryRoot(), SourcePath));
        var match = Regex.Match(source, $@"public const string {constant} = ""(?<text>(?:[^""\\]|\\.)*)"";");

        match.Success.ShouldBeTrue($"{constant} is not a string constant of {SourcePath}");
        Regex.Unescape(match.Groups["text"].Value).ShouldBe(serverText);
    }

    private static string FindRepositoryRoot()
    {
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory); directory is not null; directory = directory.Parent)
        {
            if (File.Exists(Path.Combine(directory.FullName, "42.mono.slnx")))
            {
                return directory.FullName;
            }
        }

        throw new InvalidOperationException("The repository root (42.mono.slnx) was not found above the test's directory.");
    }
}
