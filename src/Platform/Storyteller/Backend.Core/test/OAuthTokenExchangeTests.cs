using _42.Platform.Storyteller.Accessing.Federation;

namespace _42.Platform.Storyteller.Backend.Core.UnitTests;

public class OAuthTokenExchangeTests
{
    // RFC 6749 section 3.3, shared by the machine and the user token endpoints.
    [Theory]
    [InlineData("User.Impersonation", true)]
    [InlineData("User.Impersonation Annotation.Read", true)]
    [InlineData("!#[]~", true)]
    [InlineData("User.Impersonation  Annotation.Read", false)]
    [InlineData(" User.Impersonation", false)]
    [InlineData("User.Impersonation ", false)]
    [InlineData("User.\"Impersonation\"", false)]
    [InlineData("User\\Impersonation", false)]
    [InlineData("User.Impersonatión", false)]
    [InlineData("User.Impersonation\tAnnotation.Read", false)]
    [InlineData("", false)]
    public void IsScopeSyntax_AcceptsScopeTokensSeparatedBySingleSpaces(string scope, bool valid)
    {
        OAuthTokenExchange.IsScopeSyntax(scope).ShouldBe(valid);
    }
}
