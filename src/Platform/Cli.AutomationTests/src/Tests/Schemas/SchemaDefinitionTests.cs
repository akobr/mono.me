using System.Text.Json.Nodes;
using _42.Platform.Cli.AutomationTests.Access;
using _42.Platform.Cli.AutomationTests.Running;
using _42.Testing.UI.Automation;
using _42.Testing.UI.Automation.Runtime.TUnit;

namespace _42.Platform.Cli.AutomationTests.Tests.Schemas;

// Combined Schema: sform story schema definition, the schema of an annotation combined from its layers (the type, the
// descendant schemas of its ancestors, its own), and the rights and refusals of the schema commands.
[Area(Area.Schemas)]
public sealed class SchemaDefinitionTests : CliTest
{
    // SCH-DEF-01: schema definition returns the combined schema of type, ancestor and annotation schemas.
    // In a disposable project, with a unit <unt> of <rst>, the type schema of unt requiring from_type, the descendant schema
    // of unt below <rst> requiring from_descendant and the schema of <unt> requiring from_annotation: schema definition
    // <unt> prints the combined schema of <unt> in the view, whose MergedContent has the three properties and requires all
    // three, from the three AppliedSchemas.
    [Test]
    [TestCase(324)]
    [UserRole(Role.Owner)]
    [DisposableProject]
    [Covers("story schema definition", "arg0")]
    public async Task SchemaDefinition_CombinesSchemas()
    {
        await CreateDisposableProjectAsync();
        var rst = CliData.ResponsibilityKey();
        var unt = CliData.UnitKeyOf(rst);
        (await Sform(InProject("story", "set", rst))).ShouldSucceed();
        (await Sform(InProject("story", "set", unt))).ShouldSucceed();
        await WriteFileAsync("type.json", SchemaChecks.RequiringSchema("from_type"));
        await WriteFileAsync("descendant.json", SchemaChecks.RequiringSchema("from_descendant"));
        await WriteFileAsync("annotation.json", SchemaChecks.RequiringSchema("from_annotation"));
        (await Sform(InProject("story", "schema", "type", "set", "unt", "-i", "type.json"))).ShouldSucceed();
        (await Sform(InProject("story", "schema", "descendant", "set", rst, "unt", "-i", "descendant.json"))).ShouldSucceed();
        (await Sform(InProject("story", "schema", "annotation", "set", unt, "-i", "annotation.json"))).ShouldSucceed();

        var definition = (await Sform(InProject("story", "schema", "definition", unt))).ShouldSucceed();
        var combined = definition.JsonNode();
        await Assert.That(combined["AnnotationKey"]?.GetValue<string>()).IsEqualTo(unt);
        await Assert.That(combined["View"]?.GetValue<string>()).IsEqualTo(View);
        await Assert.That((combined["AppliedSchemas"] as JsonArray)?.Count).IsEqualTo(3);

        var merged = combined["MergedContent"];
        string[] layers = ["from_type", "from_descendant", "from_annotation"];
        foreach (var property in layers)
        {
            await Assert.That(merged?["properties"]?[property]).IsNotNull();
        }

        var required = (merged?["required"] as JsonArray)?.Select(item => item?.GetValue<string>() ?? string.Empty).ToArray() ?? [];
        await Assert.That(required).IsEquivalentTo(layers);
    }

    // SCH-DEF-02: A Reader reads schemas, and a Reader's schema type set is refused.
    // As a Reader, schema type get unt is not refused: where the Reader acts in Target:Project, an Editor first sets the
    // type schema of unt in a view of the test's own and the Reader gets it; in a project of Target:RoleProjects, which
    // has no writer, the read answers "The schema for 'unt' has not been found." with exit 2 when the view has none. The
    // Reader's schema type set unt -i s.json there ends with exit 44, the server's "No Contributor access to '<project>'."
    // and "Error code: AccessDenied" (a Debug API skips the role checks, automation-tests.md).
    [Test]
    [TestCase(325)]
    [UserRole(Role.Reader)]
    [Covers("story schema type get")]
    [Covers("story schema type set")]
    public async Task Schema_ReaderRights()
    {
        var schema = SchemaChecks.ObjectSchema("probe");
        await WriteFileAsync("s.json", schema);
        var view = View;
        var arranged = RoleProject is null;
        if (arranged)
        {
            // A view of its own: a type schema applies to every configuration of the type in its view.
            var editor = await ArrangeAsEditorAsync();
            view = CliData.ViewName();
            (await editor.Sform("story", "schema", "type", "set", "unt", "-i", "s.json", "-p", ProjectKey, "-v", view)).ShouldSucceed();
        }

        var get = await Sform("story", "schema", "type", "get", "unt", "-p", ProjectKey, "-v", view);
        if (arranged)
        {
            get.ShouldSucceed().ShouldPrintSchema(schema, 1);
        }
        else
        {
            get.ShouldExitWithAnyOf(CliExit.Success, CliExit.WrongInput).ShouldNotContain("AccessDenied");
            if (!get.Succeeded)
            {
                get.ShouldContain("The schema for 'unt' has not been found.");
            }
        }

        (await Sform("story", "schema", "type", "set", "unt", "-i", "s.json", "-p", ProjectKey, "-v", view))
            .ShouldExitWith(CliExit.Unauthorized)
            .ShouldContain($"No Contributor access to '{ProjectKey}'.")
            .ShouldContain("Error code: AccessDenied");
    }

    // SCH-DEF-03: A machine needs Schema.Write with Configuration.Write to set a schema.
    // An Administrator creates two API key machines of Target:Project for a view of the test's own (STORYTELLER_VIEW; a type
    // schema applies to its whole view): as the -w machine, schema type set unt -i s.json is refused with "This operation
    // needs the scope Schema.Write, which the machine was not created with." and "Error code: AccessDenied", exit 44; as
    // the -w -m SchemaWrite machine it stores the schema (version 1 of the view). The machines are deleted at the end.
    [Test]
    [TestCase(326)]
    [MachineOnly]
    [Covers("story schema type set")]
    public async Task Schema_MachineScopes()
    {
        var schema = SchemaChecks.ObjectSchema("probe");
        await WriteFileAsync("s.json", schema);
        var admin = await AsAnotherUserAsync(AdminUser);
        var view = CliData.ViewName();

        var writer = await CreateMachineAsync(new MachineCreation { As = admin, View = view }, "-w");
        (await Sform(AsMachine(writer.SignInCredentials), "story", "schema", "type", "set", "unt", "-i", "s.json"))
            .ShouldExitWith(CliExit.Unauthorized)
            .ShouldContain("This operation needs the scope Schema.Write, which the machine was not created with.")
            .ShouldContain("Error code: AccessDenied");

        var schemaWriter = await CreateMachineAsync(new MachineCreation { As = admin, View = view }, "-w", "-m", "SchemaWrite");
        var stored = (await Sform(AsMachine(schemaWriter.SignInCredentials), "story", "schema", "type", "set", "unt", "-i", "s.json"))
            .ShouldSucceed()
            .ShouldPrintSchema(schema, 1)
            .JsonNode();
        await Assert.That(stored["View"]?.GetValue<string>()).IsEqualTo(view);
    }

    // SCH-DEF-04: A file that is not a valid JSON Schema fails with exit 2 and stores nothing.
    // In a disposable project, schema type set unt -i not-a-schema.json (JSON whose $ref points to a definition that does
    // not exist) ends with exit 2, "Invalid JSON Schema: …" and "Error code: InvalidSchema"; versions unt then has no
    // versions.
    [Test]
    [TestCase(327)]
    [UserRole(Role.Owner)]
    [DisposableProject]
    [Covers("story schema type set", "-i")]
    public async Task Schema_InvalidFile_Fails()
    {
        await CreateDisposableProjectAsync();
        await WriteFileAsync(
            "not-a-schema.json",
            """{ "type": "object", "definitions": { "a": { "type": "string" } }, "properties": { "p": { "$ref": "#/definitions/missing" } } }""");

        (await Sform(InProject("story", "schema", "type", "set", "unt", "-i", "not-a-schema.json")))
            .ShouldExitWith(CliExit.WrongInput)
            .ShouldContain("Invalid JSON Schema: ")
            .ShouldContain("Error code: InvalidSchema");
        (await Sform(InProject("story", "schema", "type", "versions", "unt"))).ShouldListVersions();
    }

    // SCH-DEF-05: A type code outside rst, unt, sbt, usg, cnt, exe, uxe fails with exit 2.
    // As a Reader, schema type get abc ends with exit 2 before any call, with "Please specify a valid annotation type code."
    // and "Known codes: …".
    [Test]
    [TestCase(328)]
    [UserRole(Role.Reader)]
    [Covers("story schema type get", "arg0")]
    public async Task Schema_InvalidTypeCode_Fails()
    {
        (await Sform(InProject("story", "schema", "type", "get", "abc")))
            .ShouldExitWith(CliExit.WrongInput)
            .ShouldContain("Please specify a valid annotation type code.")
            .ShouldContain("Known codes:")
            .ShouldContain("rst");
    }
}
