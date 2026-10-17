using _42.Platform.Storyteller.Transferring;
using Newtonsoft.Json.Linq;

namespace _42.Platform.Storyteller.Backend.Core.UnitTests.Transferring;

public class ViewImportOverlayTests
{
    [Fact]
    public void AddActiveAnnotation_RecordsTheRecreatedKeysAmongTheActiveOnes()
    {
        var overlay = new ViewImportOverlay();

        overlay.AddActiveAnnotation(AnnotationKey.Parse("rst.payments"));
        overlay.AddActiveAnnotation(AnnotationKey.Parse("sbt.northwind"), isRecreated: true);
        overlay.AddActiveAnnotation(AnnotationKey.Parse("rst.payments"));

        overlay.ActiveAnnotations.ShouldBe(["rst.payments", "sbt.northwind"], ignoreOrder: true);
        overlay.RecreatedAnnotations.ShouldBe(["sbt.northwind"]);
        overlay.FailedAnnotations.ShouldBeEmpty();
    }

    [Fact]
    public void AddFailedAnnotation_KeepsTheKeyApartFromTheActiveOnes()
    {
        var overlay = new ViewImportOverlay();

        overlay.AddFailedAnnotation(AnnotationKey.Parse("usg.northwind.payments"));

        overlay.FailedAnnotations.ShouldBe(["usg.northwind.payments"]);
        overlay.ActiveAnnotations.ShouldBeEmpty();
    }

    [Fact]
    public void Documents_AreReplacedAndRemovedByTheirKeys()
    {
        var overlay = new ViewImportOverlay();
        var schema = SchemaKey.ForDescendantType("sbt.northwind", "exe");

        overlay.SetSchema(schema, JObject.Parse("""{ "type": "object" }"""));
        overlay.SetSchema(SchemaKey.ForDescendantType("sbt.northwind", "exe"), JObject.Parse("""{ "type": "string" }"""));
        overlay.SetSchema(SchemaKey.ForType("exe"), JObject.Parse("""{ "type": "object" }"""));
        overlay.SetConfiguration(AnnotationKey.Parse("rst.payments"), JObject.Parse("""{ "a": 1 }"""));

        overlay.Schemas.Count.ShouldBe(2);
        overlay.Schemas[schema]["type"]!.Value<string>().ShouldBe("string");
        overlay.Configurations["rst.payments"]["a"]!.Value<int>().ShouldBe(1);

        overlay.RemoveSchema(schema).ShouldBeTrue();
        overlay.RemoveSchema(schema).ShouldBeFalse();
        overlay.RemoveConfiguration(AnnotationKey.Parse("rst.payments")).ShouldBeTrue();

        overlay.Schemas.Keys.ShouldBe([SchemaKey.ForType("exe")]);
        overlay.Configurations.ShouldBeEmpty();
    }

    [Fact]
    public void SchemaKey_TellsTheKindsApart()
    {
        SchemaKey.ForType("exe").ShouldBe(new SchemaKey(ViewDocumentKind.TypeSchema, "exe", null));
        SchemaKey.ForAnnotation("rst.payments").ShouldBe(new SchemaKey(ViewDocumentKind.AnnotationSchema, null, "rst.payments"));
        SchemaKey.ForDescendantType("rst.payments", "unt").ShouldNotBe(SchemaKey.ForAnnotation("rst.payments"));
    }
}
