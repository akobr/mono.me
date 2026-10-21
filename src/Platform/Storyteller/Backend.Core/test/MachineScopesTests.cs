using _42.Platform.Storyteller.Accessing;

namespace _42.Platform.Storyteller.Backend.Core.UnitTests;

public class MachineScopesTests
{
    [Theory]
    [InlineData(MachineScope.AnnotationRead, "Annotation.Read")]
    [InlineData(MachineScope.AnnotationWrite, "Annotation.Write")]
    [InlineData(MachineScope.ConfigurationRead, "Configuration.Read")]
    [InlineData(MachineScope.ConfigurationWrite, "Configuration.Write")]
    [InlineData(MachineScope.SecretsRead, "Secrets.Read")]
    [InlineData(MachineScope.SecretsWrite, "Secrets.Write")]
    [InlineData(MachineScope.ConfigurationTransfer, "Configuration.Transfer")]
    [InlineData(MachineScope.SchemaWrite, "Schema.Write")]
    [InlineData(MachineScope.TemplateWrite, "Template.Write")]
    [InlineData(MachineScope.AnnotationManage, "Annotation.Manage")]
    [InlineData(MachineScope.KeyValueManage, "KeyValue.Manage")]
    public void ToScopeString_EveryScope_IsItsOneScopeStringAndParsesBack(MachineScope scope, string expected)
    {
        MachineScopes.ToScopeString(scope).ShouldBe(expected);

        MachineScopes.TryParseScopeString(expected, out var parsed).ShouldBeTrue();
        parsed.ShouldBe(scope);
    }

    // 1:1: eleven values, eleven distinct strings, and each string parses back to its value.
    [Fact]
    public void ToScopeString_IsOneToOne()
    {
        var strings = Enum.GetValues<MachineScope>().Select(MachineScopes.ToScopeString).ToList();

        strings.Count.ShouldBe(11);
        strings.ShouldAllBe(value => value != null);
        strings.Distinct(StringComparer.Ordinal).Count().ShouldBe(11);

        foreach (var scope in Enum.GetValues<MachineScope>())
        {
            MachineScopes.TryParseScopeString(MachineScopes.ToScopeString(scope)!, out var parsed).ShouldBeTrue();
            parsed.ShouldBe(scope);
        }
    }

    [Fact]
    public void ToScopeString_UndefinedScope_IsNull()
    {
        MachineScopes.ToScopeString((MachineScope)99).ShouldBeNull();
    }

    // Ordinal and exact: no case folding, no enum names, no old strings, no user scope.
    [Theory]
    [InlineData("annotation.read")]
    [InlineData("ANNOTATION.READ")]
    [InlineData("AnnotationRead")]
    [InlineData(" Annotation.Read")]
    [InlineData("Annotation.Read ")]
    [InlineData("Annotation.ReadWrite")]
    [InlineData("Default.Read")]
    [InlineData("Default.ReadWrite")]
    [InlineData("Configuration.Secrets")]
    [InlineData("User.Impersonation")]
    [InlineData("App.Annotation.Read")]
    [InlineData("")]
    [InlineData(null)]
    public void TryParseScopeString_NotAScopeString_IsFalse(string? value)
    {
        MachineScopes.TryParseScopeString(value, out _).ShouldBeFalse();
    }

    [Fact]
    public void Groups_AreInEnumOrder()
    {
        MachineScopes.Runtime.ShouldBe(
        [
            MachineScope.AnnotationRead,
            MachineScope.AnnotationWrite,
            MachineScope.ConfigurationRead,
            MachineScope.ConfigurationWrite,
            MachineScope.SecretsRead,
            MachineScope.SecretsWrite,
        ]);

        MachineScopes.Management.ShouldBe(
        [
            MachineScope.ConfigurationTransfer,
            MachineScope.SchemaWrite,
            MachineScope.TemplateWrite,
            MachineScope.AnnotationManage,
            MachineScope.KeyValueManage,
        ]);

        MachineScopes.All.ShouldBe(Enum.GetValues<MachineScope>());
        MachineScopes.All.ShouldBe(MachineScopes.Runtime.Concat(MachineScopes.Management));
    }

    // Every scope is in exactly one group.
    [Fact]
    public void IsRuntime_IsManagement_SplitEveryScope()
    {
        foreach (var scope in Enum.GetValues<MachineScope>())
        {
            MachineScopes.IsRuntime(scope).ShouldBe(MachineScopes.Runtime.Contains(scope));
            MachineScopes.IsManagement(scope).ShouldBe(MachineScopes.Management.Contains(scope));
            MachineScopes.IsRuntime(scope).ShouldNotBe(MachineScopes.IsManagement(scope), $"{scope} is in one group");
        }

        MachineScopes.IsRuntime((MachineScope)99).ShouldBeFalse();
        MachineScopes.IsManagement((MachineScope)99).ShouldBeFalse();
    }

    [Theory]
    [InlineData(MachineScope.AnnotationRead, false)]
    [InlineData(MachineScope.AnnotationWrite, false)]
    [InlineData(MachineScope.ConfigurationRead, false)]
    [InlineData(MachineScope.ConfigurationWrite, false)]
    [InlineData(MachineScope.SecretsRead, true)]
    [InlineData(MachineScope.SecretsWrite, true)]
    [InlineData(MachineScope.ConfigurationTransfer, false)]
    [InlineData(MachineScope.SchemaWrite, false)]
    [InlineData(MachineScope.TemplateWrite, false)]
    [InlineData(MachineScope.AnnotationManage, false)]
    [InlineData(MachineScope.KeyValueManage, false)]
    public void IsSecrets_OnlyTheSecretsScopes(MachineScope scope, bool expected)
    {
        MachineScopes.IsSecrets(scope).ShouldBe(expected);
    }

    [Fact]
    public void Normalize_KeepsTheDefinedValuesDistinctAndInEnumOrder()
    {
        var normalized = MachineScopes.Normalize(
        [
            MachineScope.KeyValueManage,
            (MachineScope)99,
            MachineScope.ConfigurationRead,
            MachineScope.SecretsWrite,
            MachineScope.KeyValueManage,
            MachineScope.AnnotationRead,
        ]);

        normalized.ShouldBe([MachineScope.AnnotationRead, MachineScope.ConfigurationRead, MachineScope.SecretsWrite, MachineScope.KeyValueManage]);
    }

    // Normalize does not add the implied reads; only WithImpliedReads does.
    [Fact]
    public void Normalize_AddsNothing()
    {
        MachineScopes.Normalize([MachineScope.AnnotationWrite]).ShouldBe([MachineScope.AnnotationWrite]);
    }

    [Fact]
    public void Normalize_NothingLeft_IsEmpty()
    {
        MachineScopes.Normalize(null).ShouldBeEmpty();
        MachineScopes.Normalize([]).ShouldBeEmpty();
        MachineScopes.Normalize([(MachineScope)99, (MachineScope)(-1)]).ShouldBeEmpty();
    }

    [Theory]
    [InlineData(MachineScope.AnnotationWrite, MachineScope.AnnotationRead)]
    [InlineData(MachineScope.ConfigurationWrite, MachineScope.ConfigurationRead)]
    [InlineData(MachineScope.SecretsWrite, MachineScope.SecretsRead)]
    public void WithImpliedReads_EveryWrite_AddsItsRead(MachineScope write, MachineScope read)
    {
        MachineScopes.WithImpliedReads([write]).ShouldBe([read, write]);
    }

    // Secrets imply no Configuration scope, and a read implies no write.
    [Theory]
    [InlineData(MachineScope.AnnotationRead)]
    [InlineData(MachineScope.ConfigurationRead)]
    [InlineData(MachineScope.SecretsRead)]
    public void WithImpliedReads_Read_AddsNothing(MachineScope read)
    {
        MachineScopes.WithImpliedReads([read]).ShouldBe([read]);
    }

    [Fact]
    public void WithImpliedReads_Management_AddsNothing()
    {
        foreach (var scope in MachineScopes.Management)
        {
            MachineScopes.WithImpliedReads([scope]).ShouldBe([scope]);
        }

        MachineScopes.WithImpliedReads(MachineScopes.Management).ShouldBe(MachineScopes.Management);
    }

    [Fact]
    public void WithImpliedReads_IsNormalized()
    {
        var completed = MachineScopes.WithImpliedReads(
        [
            MachineScope.SchemaWrite,
            MachineScope.SecretsWrite,
            (MachineScope)99,
            MachineScope.ConfigurationWrite,
            MachineScope.SchemaWrite,
        ]);

        completed.ShouldBe(
        [
            MachineScope.ConfigurationRead,
            MachineScope.ConfigurationWrite,
            MachineScope.SecretsRead,
            MachineScope.SecretsWrite,
            MachineScope.SchemaWrite,
        ]);
    }

    [Fact]
    public void WithImpliedReads_IsIdempotent()
    {
        foreach (var scope in Enum.GetValues<MachineScope>())
        {
            var once = MachineScopes.WithImpliedReads([scope]);
            MachineScopes.WithImpliedReads(once).ShouldBe(once);
        }

        var all = MachineScopes.WithImpliedReads(Enum.GetValues<MachineScope>());
        all.ShouldBe(MachineScopes.All);
        MachineScopes.WithImpliedReads(all).ShouldBe(all);
    }

    [Fact]
    public void WithImpliedReads_NothingLeft_IsEmpty()
    {
        MachineScopes.WithImpliedReads(null).ShouldBeEmpty();
        MachineScopes.WithImpliedReads([]).ShouldBeEmpty();
        MachineScopes.WithImpliedReads([(MachineScope)99]).ShouldBeEmpty();
    }

    [Fact]
    public void FindUndefined_ReturnsTheFirstUndefinedValue()
    {
        MachineScopes.FindUndefined([MachineScope.SchemaWrite, (MachineScope)99, (MachineScope)11])
            .ShouldBe((MachineScope)99);
    }

    [Fact]
    public void FindUndefined_OnlyDefinedOrNone_IsNull()
    {
        MachineScopes.FindUndefined(null).ShouldBeNull();
        MachineScopes.FindUndefined([]).ShouldBeNull();
        MachineScopes.FindUndefined(Enum.GetValues<MachineScope>()).ShouldBeNull();
    }

    [Fact]
    public void ToScopeStrings_KeepsTheGivenOrder_EachOnce_SkipsUndefined()
    {
        var strings = MachineScopes.ToScopeStrings(
        [
            MachineScope.KeyValueManage,
            MachineScope.AnnotationRead,
            (MachineScope)99,
            MachineScope.KeyValueManage,
        ]);

        strings.ShouldBe(["KeyValue.Manage", "Annotation.Read"]);
    }

    [Fact]
    public void ToScopeStrings_All_IsEveryScopeStringInEnumOrder()
    {
        MachineScopes.ToScopeStrings(MachineScopes.All).ShouldBe(
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

        MachineScopes.ToScopeStrings([]).ShouldBeEmpty();
    }

    // The names are the wire format of the API, the SDKs and the stored machines, and the configuration keys of
    // the Entra app roles; the values are what the API accepts as JSON integers.
    [Fact]
    public void MachineScope_KeepsItsNamesAndValues()
    {
        Enum.GetNames<MachineScope>().ShouldBe(
        [
            "AnnotationRead",
            "AnnotationWrite",
            "ConfigurationRead",
            "ConfigurationWrite",
            "SecretsRead",
            "SecretsWrite",
            "ConfigurationTransfer",
            "SchemaWrite",
            "TemplateWrite",
            "AnnotationManage",
            "KeyValueManage",
        ]);
        Enum.GetValues<MachineScope>().Select(scope => (int)scope).ShouldBe([0, 1, 2, 3, 4, 5, 6, 7, 8, 9, 10]);
    }
}
