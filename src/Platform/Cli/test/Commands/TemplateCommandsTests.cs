using System;
using System.IO.Abstractions;
using System.Threading.Tasks;
using _42.CLI.Toolkit.Output;
using _42.Platform.Cli.Commands.Templates;
using _42.Platform.Cli.Configuration;
using _42.Platform.Cli.Services;
using _42.Platform.Storyteller.Sdk;
using Microsoft.Extensions.Options;
using Moq;
using Newtonsoft.Json.Linq;
using Shouldly;
using Xunit;

namespace _42.Platform.Cli.UnitTests.Commands;

// set, edit and delete of a template by a machine without the management scope Template.Write
public class TemplateCommandsTests
{
    private const string Type = "exe";

    private readonly AnnotationCommandFixture _fixture = new();
    private readonly Mock<ITemplatesApiClient> _templates = new(MockBehavior.Strict);

    private static string Refusal => AnnotationCommandFixture.MissingScopeMessage("Template.Write");

    [Fact]
    public async Task Delete_WithoutTheManagementScope_PrintsTheServerMessage()
    {
        _templates
            .Setup(api => api.DeleteTemplateAsync("org1", "proj1", "default", Type))
            .ThrowsAsync(AnnotationCommandFixture.MissingScope("Template.Write"));

        var exitCode = await new TemplateDeleteCommand(_fixture.Console.Object, _fixture.Context.Object, _templates.Object)
        {
            AnnotationType = Type,
        }.OnExecuteAsync();

        exitCode.ShouldBe(ExitCodes.WARNING_UNAUTHORIZED_ACCESS);
        _fixture.Console.Lines.ShouldBe([$"! {Refusal}", "Error code: AccessDenied"]);
    }

    [Fact]
    public async Task Set_WithoutTheManagementScope_PrintsTheServerMessage()
    {
        _templates
            .Setup(api => api.SetTemplateAsync("org1", "proj1", "default", Type, It.IsAny<object>()))
            .ThrowsAsync(AnnotationCommandFixture.MissingScope("Template.Write"));

        var exitCode = await new TemplateSetCommand(_fixture.Console.Object, _fixture.Context.Object, _templates.Object, Mock.Of<IFileSystem>())
        {
            AnnotationType = Type,
            InlineProperties = ["owner=billing"],
        }.OnExecuteAsync();

        exitCode.ShouldBe(ExitCodes.WARNING_UNAUTHORIZED_ACCESS);
        _fixture.Console.Lines.ShouldBe([$"! {Refusal}", "Error code: AccessDenied"]);
    }

    [Fact]
    public async Task Edit_WithoutTheManagementScope_PrintsTheServerMessage()
    {
        _templates
            .Setup(api => api.GetTemplateAsync("org1", "proj1", "default", Type))
            .ReturnsAsync(new ConfigurationTemplate { Content = new JObject { ["owner"] = "billing" } });
        _templates
            .Setup(api => api.PatchTemplateAsync("org1", "proj1", "default", Type, It.IsAny<object>()))
            .ThrowsAsync(AnnotationCommandFixture.MissingScope("Template.Write"));
        var editor = new Mock<IEditorService>();
        editor
            .Setup(service => service.EditJsonAsync(
                It.IsAny<IExtendedConsole>(),
                It.IsAny<EditorOptions>(),
                It.IsAny<JObject>(),
                It.IsAny<string>(),
                false,
                It.IsAny<bool>()))
            .ReturnsAsync(new JsonEditResult(new JObject { ["owner"] = "invoicing" }, ExitCodes.SUCCESS));

        var exitCode = await new TemplateEditCommand(
            _fixture.Console.Object,
            _fixture.Context.Object,
            _templates.Object,
            editor.Object,
            Options.Create(new EditorOptions()))
        {
            AnnotationType = Type,
        }.OnExecuteAsync();

        exitCode.ShouldBe(ExitCodes.WARNING_UNAUTHORIZED_ACCESS);
        _fixture.Console.Lines.ShouldBe([$"! {Refusal}", "Error code: AccessDenied"]);
        _fixture.Console.Lines.ShouldNotContain(line => line.StartsWith("Error occurred", StringComparison.Ordinal));
    }
}
