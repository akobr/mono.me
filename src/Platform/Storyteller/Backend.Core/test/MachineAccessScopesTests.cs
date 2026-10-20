using _42.Platform.Storyteller.Accessing;

namespace _42.Platform.Storyteller.Backend.Core.UnitTests;

public class MachineAccessScopesTests
{
    private static readonly string[] ManagementStrings =
    [
        "Configuration.Transfer",
        "Schema.Write",
        "Template.Write",
        "Annotation.Manage",
        "KeyValue.Manage",
    ];

    // The presets are the runtime scopes: none of them carries a management scope.
    [Fact]
    public void Get_EveryPreset_HasNoManagementScope()
    {
        foreach (var preset in Enum.GetValues<MachineAccessScope>())
        {
            MachineAccessScopes.Get(preset).ShouldNotBeEmpty();
            MachineAccessScopes.Get(preset).Intersect(ManagementStrings).ShouldBeEmpty($"{preset} grants no management scope");
            MachineAccessScopes.Get(preset, null).ShouldBe(MachineAccessScopes.Get(preset));
            MachineAccessScopes.Get(preset, []).ShouldBe(MachineAccessScopes.Get(preset));
        }
    }

    [Theory]
    [InlineData(MachineManagementScope.ConfigurationTransfer, "Configuration.Transfer")]
    [InlineData(MachineManagementScope.SchemaWrite, "Schema.Write")]
    [InlineData(MachineManagementScope.TemplateWrite, "Template.Write")]
    [InlineData(MachineManagementScope.AnnotationManage, "Annotation.Manage")]
    [InlineData(MachineManagementScope.KeyValueManage, "KeyValue.Manage")]
    public void Get_ManagementScope_IsItsOneScopeString(MachineManagementScope scope, string expected)
    {
        MachineAccessScopes.Get(scope).ShouldBe(expected);
    }

    [Fact]
    public void Get_UndefinedManagementScope_IsNull()
    {
        MachineAccessScopes.Get((MachineManagementScope)99).ShouldBeNull();
    }

    [Fact]
    public void Get_Union_IsThePresetFirstThenTheManagementScopesInEnumOrder()
    {
        var scopes = MachineAccessScopes.Get(
            MachineAccessScope.DefaultRead,
            [MachineManagementScope.KeyValueManage, MachineManagementScope.ConfigurationTransfer, MachineManagementScope.KeyValueManage]);

        scopes.ShouldBe(["Default.Read", "Annotation.Read", "Configuration.Read", "Configuration.Transfer", "KeyValue.Manage"]);
    }

    [Fact]
    public void Get_Union_UndefinedValuesAddNothing()
    {
        var scopes = MachineAccessScopes.Get(MachineAccessScope.ConfigurationRead, [(MachineManagementScope)99, MachineManagementScope.SchemaWrite]);

        scopes.ShouldBe(["Configuration.Read", "Schema.Write"]);
    }

    [Fact]
    public void Normalize_KeepsTheDefinedValuesDistinctAndInEnumOrder()
    {
        var normalized = MachineAccessScopes.Normalize(
            [MachineManagementScope.TemplateWrite, (MachineManagementScope)99, MachineManagementScope.ConfigurationTransfer, MachineManagementScope.TemplateWrite]);

        normalized.ShouldBe([MachineManagementScope.ConfigurationTransfer, MachineManagementScope.TemplateWrite]);
    }

    [Fact]
    public void Normalize_NothingLeft_IsNull()
    {
        MachineAccessScopes.Normalize(null).ShouldBeNull();
        MachineAccessScopes.Normalize([]).ShouldBeNull();
        MachineAccessScopes.Normalize([(MachineManagementScope)99]).ShouldBeNull();
    }

    [Fact]
    public void FindUndefined_ReturnsTheFirstUndefinedValue()
    {
        MachineAccessScopes.FindUndefined([MachineManagementScope.SchemaWrite, (MachineManagementScope)99, (MachineManagementScope)7])
            .ShouldBe((MachineManagementScope)99);
    }

    [Fact]
    public void FindUndefined_OnlyDefinedOrNone_IsNull()
    {
        MachineAccessScopes.FindUndefined(null).ShouldBeNull();
        MachineAccessScopes.FindUndefined([]).ShouldBeNull();
        MachineAccessScopes.FindUndefined(Enum.GetValues<MachineManagementScope>()).ShouldBeNull();
    }

    // The names are the wire format of the API and of the stored machines.
    [Fact]
    public void ManagementScope_KeepsItsNamesAndValues()
    {
        Enum.GetNames<MachineManagementScope>().ShouldBe(["ConfigurationTransfer", "SchemaWrite", "TemplateWrite", "AnnotationManage", "KeyValueManage"]);
        Enum.GetValues<MachineManagementScope>().Select(scope => (int)scope).ShouldBe([0, 1, 2, 3, 4]);
    }
}
