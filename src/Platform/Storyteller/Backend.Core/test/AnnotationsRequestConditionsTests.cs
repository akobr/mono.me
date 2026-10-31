using _42.Platform.Storyteller.Annotating;

namespace _42.Platform.Storyteller.Backend.Core.UnitTests;

// The conditions of a list request apply to the models of their own type, combined with AND.
public class AnnotationsRequestConditionsTests
{
    [Fact]
    public void Against_IsTheTypeOfTheTypeParameter()
    {
        new AnnotationsRequest.Condition<Responsibility>().Against.ShouldBe(AnnotationType.Responsibility);
        new AnnotationsRequest.Condition<Subject>().Against.ShouldBe(AnnotationType.Subject);
        new AnnotationsRequest.Condition<Usage>().Against.ShouldBe(AnnotationType.Usage);
        new AnnotationsRequest.Condition<Context>().Against.ShouldBe(AnnotationType.Context);
        new AnnotationsRequest.Condition<Execution>().Against.ShouldBe(AnnotationType.Execution);
        new AnnotationsRequest.Condition<Unit>().Against.ShouldBe(AnnotationType.Unit);
        new AnnotationsRequest.Condition<UnitOfExecution>().Against.ShouldBe(AnnotationType.UnitOfExecution);
    }

    [Fact]
    public void CompileConditions_WithoutConditions_PassesEverything()
    {
        var predicate = new AnnotationsRequest().CompileConditions<Subject>();

        predicate(Model<Subject>("sbt.northwind")).ShouldBeTrue();
    }

    [Fact]
    public void CompileConditions_OfTheType_Filter()
    {
        var request = Request(new AnnotationsRequest.Condition<Subject> { Predicate = s => s.Name == "northwind" });

        var predicate = request.CompileConditions<Subject>();

        predicate(Model<Subject>("sbt.northwind")).ShouldBeTrue();
        predicate(Model<Subject>("sbt.contoso")).ShouldBeFalse();
    }

    [Fact]
    public void CompileConditions_OfAnotherType_DoNotApply()
    {
        var request = Request(new AnnotationsRequest.Condition<Subject> { Predicate = _ => false });

        var predicate = request.CompileConditions<Responsibility>();

        predicate(Model<Responsibility>("rst.invoicing")).ShouldBeTrue();
    }

    [Fact]
    public void CompileConditions_OfMixedTypes_ApplyEachToItsOwnType()
    {
        var request = Request(
            new AnnotationsRequest.Condition<Usage> { Predicate = u => u.SubjectKey == "sbt.northwind" },
            new AnnotationsRequest.Condition<Context> { Predicate = c => c.SubjectKey == "sbt.contoso" });

        var usages = request.CompileConditions<Usage>();
        var contexts = request.CompileConditions<Context>();

        usages(Model<Usage>("usg.northwind.invoicing")).ShouldBeTrue();
        usages(Model<Usage>("usg.contoso.invoicing")).ShouldBeFalse();
        contexts(Model<Context>("cnt.contoso.production")).ShouldBeTrue();
        contexts(Model<Context>("cnt.northwind.production")).ShouldBeFalse();
    }

    [Fact]
    public void CompileConditions_OfOneType_CombineWithAnd()
    {
        var request = Request(
            new AnnotationsRequest.Condition<Usage> { Predicate = u => u.SubjectName == "northwind" },
            new AnnotationsRequest.Condition<Usage> { Predicate = u => u.ResponsibilityName == "invoicing" });

        var predicate = request.CompileConditions<Usage>();

        predicate(Model<Usage>("usg.northwind.invoicing")).ShouldBeTrue();
        predicate(Model<Usage>("usg.northwind.shipping")).ShouldBeFalse();
        predicate(Model<Usage>("usg.contoso.invoicing")).ShouldBeFalse();
    }

    private static AnnotationsRequest Request(params AnnotationsRequest.ICondition[] conditions)
    {
        return new AnnotationsRequest { Conditions = conditions };
    }

    private static TAnnotation Model<TAnnotation>(string key)
        where TAnnotation : Annotation
    {
        return (TAnnotation)AnnotationFactory.CreateDefault(AnnotationKey.Parse(key), "billing", "default");
    }
}
