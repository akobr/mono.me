namespace _42.Platform.Storyteller.Sdk.Hosting.UnitTests.Catalog;

public sealed class TargetTests
{
    private static readonly string[] Ancestors =
    [
        "rst.invoicing",
        "unt.invoicing.daily",
        "sbt.northwind",
        "usg.northwind.invoicing",
        "cnt.northwind.prod",
        "exe.northwind.invoicing.prod",
        "uxe.northwind.invoicing.prod.daily",
    ];

    private static readonly AnnotationType[] Descendants =
    [
        AnnotationType.Usage,
        AnnotationType.Context,
        AnnotationType.Unit,
        AnnotationType.Execution,
        AnnotationType.UnitOfExecution,
    ];

    // The table of AnnotationsHttp.GetDescendants: what the API lists under each type.
    private static readonly HashSet<(string Ancestor, AnnotationType Type)> Supported =
    [
        ("rst.invoicing", AnnotationType.Usage),
        ("rst.invoicing", AnnotationType.Unit),
        ("rst.invoicing", AnnotationType.Execution),
        ("rst.invoicing", AnnotationType.UnitOfExecution),
        ("unt.invoicing.daily", AnnotationType.UnitOfExecution),
        ("sbt.northwind", AnnotationType.Usage),
        ("sbt.northwind", AnnotationType.Context),
        ("sbt.northwind", AnnotationType.Execution),
        ("sbt.northwind", AnnotationType.UnitOfExecution),
        ("usg.northwind.invoicing", AnnotationType.Execution),
        ("usg.northwind.invoicing", AnnotationType.UnitOfExecution),
        ("cnt.northwind.prod", AnnotationType.Execution),
        ("cnt.northwind.prod", AnnotationType.UnitOfExecution),
        ("exe.northwind.invoicing.prod", AnnotationType.UnitOfExecution),
    ];

    [Fact]
    public void EveryCombinationOfTheApi_IsATarget_AndNoOther()
    {
        foreach (var ancestor in Ancestors)
        {
            foreach (var type in Descendants)
            {
                if (Supported.Contains((ancestor, type)))
                {
                    Target(ancestor, type).ShouldBe((ancestor, type));
                }
                else
                {
                    Should.Throw<ArgumentException>(() => Target(ancestor, type), $"{type} of {ancestor}").ParamName.ShouldBe("key");
                }
            }
        }
    }

    [Fact]
    public void EachFactory_TargetsItsType_WithNoFilter()
    {
        var target = StorytellerTarget.UnitsOf("rst.invoicing");

        target.Of.ShouldBe("rst.invoicing");
        target.Type.ShouldBe(AnnotationType.Unit);
        target.Subject.ShouldBeNull();
        target.Responsibility.ShouldBeNull();
        target.Context.ShouldBeNull();
        target.Unit.ShouldBeNull();
        target.IncludeInactive.ShouldBeFalse();
        StorytellerTarget.UsagesOf("sbt.northwind").Type.ShouldBe(AnnotationType.Usage);
        StorytellerTarget.ContextsOf("sbt.northwind").Type.ShouldBe(AnnotationType.Context);
        StorytellerTarget.ExecutionsOf("usg.northwind.invoicing").Type.ShouldBe(AnnotationType.Execution);
        StorytellerTarget.UnitsOfExecutionOf("exe.northwind.invoicing.prod").Type.ShouldBe(AnnotationType.UnitOfExecution);
    }

    [Theory]
    [InlineData(" RST.invoicing ", "rst.invoicing")]
    [InlineData("@acme.billing.default.RST.invoicing", "@acme.billing.default.rst.invoicing")]
    [InlineData("@acme.billing.default.rst.invoicing", "@acme.billing.default.rst.invoicing")]
    public void TheKey_IsTrimmed_WithTheTypeCodeLowerCased(string key, string of)
    {
        StorytellerTarget.UnitsOf(key).Of.ShouldBe(of);
    }

    [Theory]
    [InlineData("")]
    [InlineData("  ")]
    [InlineData("rst")]
    [InlineData("nope.invoicing")]
    [InlineData("unt.invoicing")]
    [InlineData("@acme.billing.rst.invoicing")]
    public void AKeyThatDoesNotParse_IsRefused(string key)
    {
        Should.Throw<ArgumentException>(() => StorytellerTarget.UnitsOf(key)).ParamName.ShouldBe("key");
    }

    [Fact]
    public void AKeyWithoutThoseDescendants_IsRefused_NamingWhatItHas()
    {
        Should.Throw<ArgumentException>(() => StorytellerTarget.ContextsOf("rst.invoicing")).Message
            .ShouldStartWith("rst.invoicing has no contexts: the descendants of a responsibility are its units, usages, executions and units of execution.");
        Should.Throw<ArgumentException>(() => StorytellerTarget.ExecutionsOf("unt.invoicing.daily")).Message
            .ShouldStartWith("unt.invoicing.daily has no executions: the descendants of a unit are its units of execution.");
        Should.Throw<ArgumentException>(() => StorytellerTarget.UnitsOfExecutionOf("uxe.northwind.invoicing.prod.daily")).Message
            .ShouldStartWith("uxe.northwind.invoicing.prod.daily is a unit of execution, which has no descendants.");
    }

    [Fact]
    public void Filters_SetOneNameEach_OnANewTarget()
    {
        var target = StorytellerTarget.UnitsOfExecutionOf("rst.invoicing");

        var filtered = target.WithSubject("northwind").WithContext("prod").WithUnit("daily").IncludingInactive();

        filtered.ShouldNotBeSameAs(target);
        filtered.Of.ShouldBe("rst.invoicing");
        filtered.Type.ShouldBe(AnnotationType.UnitOfExecution);
        filtered.Subject.ShouldBe("northwind");
        filtered.Context.ShouldBe("prod");
        filtered.Unit.ShouldBe("daily");
        filtered.Responsibility.ShouldBeNull();
        filtered.IncludeInactive.ShouldBeTrue();
        target.Subject.ShouldBeNull();
        target.Context.ShouldBeNull();
        target.Unit.ShouldBeNull();
        target.IncludeInactive.ShouldBeFalse();
        target.WithSubject("contoso").WithSubject("northwind").Subject.ShouldBe("northwind");
    }

    [Fact]
    public void AFilterOfANameTheDescendantsKeysDoNotHave_IsRefused()
    {
        var unit = Should.Throw<ArgumentException>(() => StorytellerTarget.UnitsOf("rst.invoicing").WithContext("prod"));
        unit.ParamName.ShouldBe("context");
        unit.Message.ShouldStartWith("The keys of units, unt.{responsibility}.{unit}, name no context, so they have no context to filter by.");

        Should.Throw<ArgumentException>(() => StorytellerTarget.UnitsOf("rst.invoicing").WithSubject("northwind")).ParamName.ShouldBe("subject");
        Should.Throw<ArgumentException>(() => StorytellerTarget.UsagesOf("rst.invoicing").WithUnit("daily")).ParamName.ShouldBe("unit");
        Should.Throw<ArgumentException>(() => StorytellerTarget.ContextsOf("sbt.northwind").WithResponsibility("invoicing")).ParamName.ShouldBe("responsibility");
        Should.Throw<ArgumentException>(() => StorytellerTarget.ExecutionsOf("sbt.northwind").WithUnit("daily")).ParamName.ShouldBe("unit");
    }

    [Fact]
    public void AFilterOfANameTheAncestorFixes_IsRefused()
    {
        var responsibility = Should.Throw<ArgumentException>(() => StorytellerTarget.ExecutionsOf("rst.invoicing").WithResponsibility("invoicing"));
        responsibility.ParamName.ShouldBe("responsibility");
        responsibility.Message.ShouldStartWith("rst.invoicing names the responsibility of its executions already, invoicing; leave out the filter.");

        Should.Throw<ArgumentException>(() => StorytellerTarget.ExecutionsOf("sbt.northwind").WithSubject("northwind")).ParamName.ShouldBe("subject");
        Should.Throw<ArgumentException>(() => StorytellerTarget.ExecutionsOf("cnt.northwind.prod").WithContext("prod")).ParamName.ShouldBe("context");
        Should.Throw<ArgumentException>(() => StorytellerTarget.UnitsOfExecutionOf("unt.invoicing.daily").WithUnit("daily")).ParamName.ShouldBe("unit");
        Should.Throw<ArgumentException>(() => StorytellerTarget.UnitsOfExecutionOf("exe.northwind.invoicing.prod").WithSubject("northwind")).ParamName.ShouldBe("subject");
    }

    [Theory]
    [InlineData("")]
    [InlineData(" ")]
    [InlineData("prod.eu")]
    public void AFilterThatIsNotOneSegment_IsRefused(string name)
    {
        Should.Throw<ArgumentException>(() => StorytellerTarget.ExecutionsOf("rst.invoicing").WithContext(name)).ParamName.ShouldBe("context");
    }

    [Fact]
    public void ToString_DescribesTheTarget()
    {
        StorytellerTarget.UnitsOf("rst.invoicing").ToString().ShouldBe("the units of rst.invoicing");
        StorytellerTarget.UnitsOfExecutionOf("@acme.billing.default.rst.invoicing").WithSubject("northwind").WithContext("prod").WithUnit("daily").IncludingInactive().ToString()
            .ShouldBe("the units of execution of @acme.billing.default.rst.invoicing of the subject northwind in the context prod of the unit daily, inactive ones included");
        StorytellerTarget.ExecutionsOf("sbt.northwind").WithResponsibility("invoicing").ToString().ShouldBe("the executions of sbt.northwind of the responsibility invoicing");
    }

    [Theory]
    [InlineData("exe.northwind.invoicing.prod", true)]
    [InlineData("exe.contoso.invoicing.prod", false)]
    [InlineData("exe.northwind.invoicing.dev", false)]
    [InlineData("exe.northwind.invoicing.Prod", false)]
    public void TheFilters_CompareNamesOrdinally(string key, bool passes)
    {
        var target = StorytellerTarget.ExecutionsOf("rst.invoicing").WithSubject("northwind").WithContext("prod");

        target.Passes(AnnotationKey.Parse(key)).ShouldBe(passes);
    }

    private static (string Of, AnnotationType Type) Target(string key, AnnotationType type)
    {
        return type switch
        {
            AnnotationType.Usage => Describe(StorytellerTarget.UsagesOf(key)),
            AnnotationType.Context => Describe(StorytellerTarget.ContextsOf(key)),
            AnnotationType.Unit => Describe(StorytellerTarget.UnitsOf(key)),
            AnnotationType.Execution => Describe(StorytellerTarget.ExecutionsOf(key)),
            AnnotationType.UnitOfExecution => Describe(StorytellerTarget.UnitsOfExecutionOf(key)),
            _ => throw new ArgumentOutOfRangeException(nameof(type), type, null),
        };
    }

    private static (string Of, AnnotationType Type) Describe<TAnnotation>(StorytellerTarget<TAnnotation> target)
        where TAnnotation : StorytellerAnnotation
    {
        return (target.Of, target.Type);
    }
}
