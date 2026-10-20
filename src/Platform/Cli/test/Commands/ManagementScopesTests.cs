using System;
using System.Linq;
using _42.Platform.Cli.Commands.MachineAccess;
using _42.Platform.Storyteller.Sdk;
using Shouldly;
using Xunit;

namespace _42.Platform.Cli.UnitTests.Commands;

public class ManagementScopesTests
{
    [Fact]
    public void ScopeStrings_OfEveryScopeOfTheSdk_AreTheStringsOfTheApi()
    {
        Enum.GetValues<MachineManagementScope>()
            .Select(ManagementScopes.ToScopeString)
            .ShouldBe(["Configuration.Transfer", "Schema.Write", "Template.Write", "Annotation.Manage", "KeyValue.Manage"]);
    }

    [Fact]
    public void Names_ListEveryScopeOfTheSdk()
    {
        Enum.GetNames<MachineManagementScope>().ShouldAllBe(name => ManagementScopes.Names.Contains(name));
    }

    [Fact]
    public void ScopeStrings_AreParsedBackToTheirScope()
    {
        var scopes = Enum.GetValues<MachineManagementScope>();

        ManagementScopes.TryParse(scopes.Select(ManagementScopes.ToScopeString), out var parsed, out var unknown).ShouldBeTrue();

        unknown.ShouldBeNull();
        parsed.ShouldBe(scopes);
    }

    [Fact]
    public void Missing_IsNull()
    {
        ManagementScopes.TryParse(null, out var parsed, out var unknown).ShouldBeTrue();

        parsed.ShouldBeNull();
        unknown.ShouldBeNull();
    }
}
