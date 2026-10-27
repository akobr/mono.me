#nullable enable

using System;
using System.Text;
using FluentAssertions;
using Xunit;

namespace _42.Platform.Storyteller.Backend.CosmosDb.UnitTests;

// The continuation token of annotation lists, without Cosmos DB: its text is what the next request sends back.
public class ContinuationTokenTests
{
    // a token of a cross-partition query, as Cosmos DB writes it
    private const string CosmosToken = """[{"token":"+RID:~a2ZpAJ5iGQkDAAAAAAAAAA==#RT:1#TRC:2#ISV:2#IEO:65567#QCF:8","range":{"min":"","max":"FF"}}]""";

    [Fact]
    public void AToken_ParsesBackFromItsText_ToTheSameTypeAndCosmosToken()
    {
        var text = new ContinuationToken("Execution", CosmosToken).ToString();

        ContinuationToken.TryParse(text, out var parsed).Should().BeTrue();
        parsed!.Type.Should().Be("Execution");
        parsed.CosmosToken.Should().Be(CosmosToken);
        parsed.IsFirstPage.Should().BeFalse();
    }

    [Fact]
    public void TheText_IsTheBase64OfTheTypeAndTheCosmosToken()
    {
        var text = new ContinuationToken("Usage", "abc").ToString();

        text.Should().Be(Convert.ToBase64String(Encoding.UTF8.GetBytes("Usage|abc")));
    }

    [Fact]
    public void TheFirstPage_RoundTrips()
    {
        var text = ContinuationToken.CreateFirstPage("Context").ToString();

        ContinuationToken.TryParse(text, out var parsed).Should().BeTrue();
        parsed!.Type.Should().Be("Context");
        parsed.IsFirstPage.Should().BeTrue();
    }

    [Fact]
    public void ACosmosTokenWithTheSeparator_RoundTrips()
    {
        var text = new ContinuationToken("Subject", "left|middle|right").ToString();

        ContinuationToken.TryParse(text, out var parsed).Should().BeTrue();
        parsed!.Type.Should().Be("Subject");
        parsed.CosmosToken.Should().Be("left|middle|right");
    }

    [Fact]
    public void AnEmptyCosmosToken_IsTheFirstPage()
    {
        var text = Convert.ToBase64String(Encoding.UTF8.GetBytes("Unit|"));

        ContinuationToken.TryParse(text, out var parsed).Should().BeTrue();
        parsed!.Type.Should().Be("Unit");
        parsed.IsFirstPage.Should().BeTrue();
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("not a token")]
    [InlineData("Execution|first")]
    [InlineData("Execution|+RID:~a2ZpAJ5iGQkDAAAAAAAAAA==#RT:1")]
    public void GarbageAndTheOldUnencodedForm_DoNotParse(string? text)
    {
        ContinuationToken.TryParse(text, out var parsed).Should().BeFalse();
        parsed.Should().BeNull();
    }

    [Fact]
    public void Base64WithoutASeparator_DoesNotParse()
    {
        var text = Convert.ToBase64String(Encoding.UTF8.GetBytes("Execution"));

        ContinuationToken.TryParse(text, out _).Should().BeFalse();
    }
}
