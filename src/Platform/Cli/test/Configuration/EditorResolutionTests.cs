using System.Collections.Generic;
using _42.Platform.Cli.Configuration;
using Shouldly;
using Xunit;

namespace _42.Platform.Cli.UnitTests.Configuration;

// The editor of an edit: the configured one, else $VISUAL, else $EDITOR, else none (sform asks).
public class EditorResolutionTests
{
    [Fact]
    public void Visual_ComesBeforeEditor()
    {
        var environment = Environment(new() { ["VISUAL"] = " code --wait ", ["EDITOR"] = "vim" });

        environment.IsSet.ShouldBeTrue();
        environment.Variable.ShouldBe("VISUAL");
        environment.Command.ShouldBe("code --wait");
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void ABlankVisual_FallsBackToEditor(string? visual)
    {
        var environment = Environment(new() { ["VISUAL"] = visual, ["EDITOR"] = "nano -w" });

        environment.Variable.ShouldBe("EDITOR");
        environment.Command.ShouldBe("nano -w");
    }

    [Fact]
    public void NoVariables_AreNoEditor()
    {
        var environment = Environment(new() { ["VISUAL"] = " ", ["EDITOR"] = string.Empty });

        environment.IsSet.ShouldBeFalse();
        environment.Variable.ShouldBeNull();
        environment.Command.ShouldBeNull();
    }

    [Fact]
    public void AConfiguredEditor_WinsOverTheEnvironment()
    {
        var configured = new EditorOptions { EditorType = EditorType.Vim };

        var resolved = EditorResolution.Resolve(configured, Environment(new() { ["EDITOR"] = "nano" }));

        resolved.Origin.ShouldBe(EditorOrigin.Configured);
        resolved.Options.ShouldBeSameAs(configured);
    }

    [Theory]
    [InlineData(null, null)]
    [InlineData(EditorType.Custom, null)]
    [InlineData(EditorType.Custom, "  ")]
    public void AnEditorThatIsNotConfigured_FallsBackToTheEnvironment(EditorType? type, string? command)
    {
        var resolved = EditorResolution.Resolve(
            new EditorOptions { EditorType = type, CustomCommand = command },
            Environment(new() { ["EDITOR"] = "nano" }));

        resolved.Origin.ShouldBe(EditorOrigin.Environment);
        resolved.Variable.ShouldBe("EDITOR");
        resolved.Options!.EditorType.ShouldBe(EditorType.Custom);
        resolved.Options.CustomCommand.ShouldBe("nano");
    }

    [Fact]
    public void NeitherConfiguredNorInTheEnvironment_IsNone()
    {
        var resolved = EditorResolution.Resolve(new EditorOptions(), EnvironmentEditor.None);

        resolved.Origin.ShouldBe(EditorOrigin.None);
        resolved.Options.ShouldBeNull();
    }

    private static EnvironmentEditor Environment(Dictionary<string, string?> variables)
    {
        return EnvironmentEditor.FromEnvironment(name => variables.GetValueOrDefault(name));
    }
}
