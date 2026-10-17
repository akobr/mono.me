using System;
using _42.Platform.Cli.Authentication;
using Shouldly;
using Xunit;

namespace _42.Platform.Cli.UnitTests.Authentication;

public class JwtExpiryTests
{
    private static readonly DateTimeOffset Start = new(2026, 10, 17, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public void Read_ReadsExpFromTheJwtPayload()
    {
        JwtExpiry.Read(TestTokens.AccessToken(Start.AddMinutes(5))).ShouldBe(Start.AddMinutes(5));
    }

    [Theory]
    [InlineData("not-a-jwt")]
    [InlineData("a.%%%.c")]
    [InlineData("a.W10.c")]
    [InlineData("a.e30.c")]
    [InlineData("a.eyJleHAiOiIxIn0.c")]
    public void Read_WithoutAReadableExp_IsNull(string token)
    {
        // W10 is [], e30 is {} and eyJleHAiOiIxIn0 is {"exp":"1"}.
        JwtExpiry.Read(token).ShouldBeNull();
    }
}
