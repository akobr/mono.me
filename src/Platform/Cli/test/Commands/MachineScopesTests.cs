using System;
using System.Linq;
using _42.Platform.Cli.Commands.MachineAccess;
using _42.Platform.Storyteller.Sdk;
using Shouldly;
using Xunit;

namespace _42.Platform.Cli.UnitTests.Commands;

public class MachineScopesTests
{
    [Fact]
    public void ScopeStrings_OfEveryScopeOfTheSdk_AreTheStringsOfTheApi()
    {
        Enum.GetValues<MachineScope>()
            .Select(MachineScopes.ToScopeString)
            .ShouldBe(
            [
                "Annotation.Read",
                "Annotation.Write",
                "Configuration.Read",
                "Configuration.Write",
                "Secrets.Read",
                "Secrets.Write",
                "Configuration.Transfer",
                "Schema.Write",
                "Template.Write",
                "Annotation.Manage",
                "KeyValue.Manage",
            ]);
    }

    [Fact]
    public void Names_ListEveryScopeOfTheSdkInEnumOrder()
    {
        ListOf(MachineScopes.Names).ShouldBe(Enum.GetNames<MachineScope>());
    }

    [Fact]
    public void ManagementNames_ListTheManagementScopesInEnumOrder()
    {
        ListOf(MachineScopes.ManagementNames).ShouldBe(Enum.GetValues<MachineScope>().Where(MachineScopes.IsManagement).Select(scope => scope.ToString()));
    }

    [Fact]
    public void IsManagement_HoldsForTheFiveScopesOfBulkAndStructuralOperations()
    {
        Enum.GetValues<MachineScope>().Where(MachineScopes.IsManagement).ShouldBe(
        [
            MachineScope.ConfigurationTransfer,
            MachineScope.SchemaWrite,
            MachineScope.TemplateWrite,
            MachineScope.AnnotationManage,
            MachineScope.KeyValueManage,
        ]);
    }

    [Fact]
    public void ScopeStrings_AreParsedBackToTheirScope()
    {
        var scopes = Enum.GetValues<MachineScope>();

        MachineScopes.TryParse(scopes.Select(MachineScopes.ToScopeString), out var parsed, out var unknown).ShouldBeTrue();

        unknown.ShouldBeNull();
        parsed.ShouldBe(scopes);
    }

    [Fact]
    public void Names_AreParsedBackToTheirScope()
    {
        var scopes = Enum.GetValues<MachineScope>();

        MachineScopes.TryParse(scopes.Select(scope => scope.ToString()), out var parsed, out _).ShouldBeTrue();

        parsed.ShouldBe(scopes);
    }

    [Fact]
    public void Missing_IsEmpty()
    {
        MachineScopes.TryParse(null, out var parsed, out var unknown).ShouldBeTrue();

        parsed.ShouldBeEmpty();
        unknown.ShouldBeNull();
    }

    // "A, B or C" as the names of the help and the messages.
    private static string[] ListOf(string names)
    {
        return names.Replace(" or ", ", ").Split(", ");
    }
}
