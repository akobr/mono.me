#nullable enable

using System;
using FluentAssertions;
using Xunit;

namespace _42.Platform.Storyteller.Backend.CosmosDb.UnitTests;

// Pure rules, no Cosmos DB needed.
public class NameRulesTests
{
    [Theory]
    [InlineData("acme")]
    [InlineData("42dotnet")]
    [InlineData("billing-eu-2")]
    [InlineData("ab")]
    public void ValidNames_Pass(string name)
    {
        NameRules.IsValidName(name).Should().BeTrue();
        FluentActions.Invoking(() => NameRules.EnsureOrganizationName(name)).Should().NotThrow();
        FluentActions.Invoking(() => NameRules.EnsureProjectName(name)).Should().NotThrow();
        FluentActions.Invoking(() => NameRules.EnsureViewName(name)).Should().NotThrow();
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("a")]
    [InlineData("Acme")]
    [InlineData("acme.billing")]
    [InlineData("-acme")]
    [InlineData("acme_billing")]
    [InlineData("acme billing")]
    [InlineData("ěščř")]
    public void InvalidNames_ThrowInvalidName(string? name)
    {
        NameRules.IsValidName(name).Should().BeFalse();
        FluentActions.Invoking(() => NameRules.EnsureProjectName(name))
            .Should().Throw<InvalidInputException>()
            .Which.ErrorCode.Should().Be(ErrorCodes.InvalidName);
    }

    [Fact]
    public void NameLongerThan63_IsInvalid()
    {
        NameRules.IsValidName(new string('a', 63)).Should().BeTrue();
        NameRules.IsValidName(new string('a', 64)).Should().BeFalse();
    }

    [Theory]
    [InlineData("access")]
    [InlineData("orgs")]
    [InlineData("invitations")]
    public void ReservedOrganizationNames_ThrowInvalidName(string name)
    {
        FluentActions.Invoking(() => NameRules.EnsureOrganizationName(name))
            .Should().Throw<InvalidInputException>().WithMessage("*reserved*");
    }

    [Theory]
    [InlineData("views")]
    [InlineData("members")]
    [InlineData("settings")]
    public void ReservedProjectAndViewNames_ThrowInvalidName(string name)
    {
        FluentActions.Invoking(() => NameRules.EnsureProjectName(name)).Should().Throw<InvalidInputException>();
        FluentActions.Invoking(() => NameRules.EnsureViewName(name)).Should().Throw<InvalidInputException>();
    }

    [Fact]
    public void ReservedLists_DoNotOverlapWhereTheyShouldNot()
    {
        // "orgs" is a top-level route of the admin UI only; a project or view may be called that.
        FluentActions.Invoking(() => NameRules.EnsureProjectName("orgs")).Should().NotThrow();
        FluentActions.Invoking(() => NameRules.EnsureOrganizationName("views")).Should().NotThrow();
        string.Join(',', NameRules.ReservedOrganizationNames).Should().NotBeNullOrEmpty();
        StringComparer.Ordinal.Equals(NameRules.Pattern, "^[a-z0-9][a-z0-9-]{1,62}$").Should().BeTrue();
    }
}
