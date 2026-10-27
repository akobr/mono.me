using Aspire.Hosting;

namespace _42.Platform.Storyteller.AspireHosting.UnitTests;

public sealed class KeyTests
{
    private const string Endpoint = "https://storyteller.example.com/api";

    [Fact]
    public async Task AFullKey_IsUsedAsWritten_WithoutAReference()
    {
        var host = TestAppHost.Run();
        var project = host.AddProject().WithStorytellerKey("@acme.billing.default.exe.northwind.invoicing.dev");

        (await host.ReadVariablesAsync(project.Resource)).ShouldBe(new Dictionary<string, string?>
        {
            ["STORYTELLER_KEY"] = "@acme.billing.default.exe.northwind.invoicing.dev",
        });
    }

    [Fact]
    public async Task AFullKey_IsNotPrefixed_ByTheViewOfTheReference()
    {
        var host = TestAppHost.Run();
        var storyteller = host.Builder.AddStoryteller("storyteller", Endpoint).WithView("acme", "billing");
        var project = host.AddProject().WithReference(storyteller).WithStorytellerKey("@house.main.staging.rst.invoicing");

        (await host.ReadVariablesAsync(project.Resource))["STORYTELLER_KEY"].ShouldBe("@house.main.staging.rst.invoicing");
    }

    [Fact]
    public async Task AnAnnotationKey_IsPrefixed_WithTheViewOfTheReferencedResource()
    {
        var host = TestAppHost.Run();
        var storyteller = host.Builder.AddStoryteller("storyteller", Endpoint).WithView("acme", "billing", "staging");
        var project = host.AddProject().WithReference(storyteller).WithStorytellerKey("rst.invoicing");

        (await host.ReadVariablesAsync(project.Resource)).ShouldBe(new Dictionary<string, string?>
        {
            ["STORYTELLER_ENDPOINT"] = Endpoint,
            ["STORYTELLER_KEY"] = "@acme.billing.staging.rst.invoicing",
        });
    }

    [Fact]
    public async Task TheOrderOfTheCalls_DoesNotMatter()
    {
        var host = TestAppHost.Run();
        var storyteller = host.Builder.AddStoryteller("storyteller", Endpoint);
        var project = host.AddProject().WithStorytellerKey("uxe.northwind.invoicing.dev.nightly").WithReference(storyteller);
        storyteller.WithView("acme", "billing");

        (await host.ReadVariablesAsync(project.Resource))["STORYTELLER_KEY"].ShouldBe("@acme.billing.default.uxe.northwind.invoicing.dev.nightly");
    }

    [Fact]
    public async Task WithExecution_SetsTheKeyOfTheExecution()
    {
        var host = TestAppHost.Run();
        var storyteller = host.Builder.AddStoryteller("storyteller", Endpoint).WithView("acme", "billing");
        var executable = host.AddExecutable().WithReference(storyteller).WithExecution("northwind", "invoicing", "dev");

        (await host.ReadVariablesAsync(executable.Resource))["STORYTELLER_KEY"].ShouldBe("@acme.billing.default.exe.northwind.invoicing.dev");
    }

    [Fact]
    public async Task TheLastKey_Wins_AndAnEarlierKeyIsNotChecked()
    {
        var host = TestAppHost.Run();
        var project = host.AddProject()
            .WithStorytellerKey("rst.invoicing")
            .WithExecution("northwind", "invoicing", "dev")
            .WithStorytellerKey("@acme.billing.default.sbt.northwind");

        (await host.ReadVariablesAsync(project.Resource)).ShouldBe(new Dictionary<string, string?>
        {
            ["STORYTELLER_KEY"] = "@acme.billing.default.sbt.northwind",
        });
    }

    [Fact]
    public async Task AnAnnotationKey_WithoutAReference_FailsTheStart_NamingWithViewAndWithReference()
    {
        var host = TestAppHost.Run();
        var project = host.AddProject().WithStorytellerKey("rst.invoicing");

        var exception = await Should.ThrowAsync<InvalidOperationException>(() => host.BuildAsync(project.Resource));

        exception.Message.ShouldContain("'worker'");
        exception.Message.ShouldContain("WithView");
        exception.Message.ShouldContain("WithReference");
    }

    [Fact]
    public async Task AnAnnotationKey_WithAReferenceWithoutAView_FailsTheStart()
    {
        var host = TestAppHost.Run();
        var storyteller = host.Builder.AddStoryteller("storyteller", Endpoint);
        var project = host.AddProject().WithReference(storyteller).WithExecution("northwind", "invoicing", "dev");

        var exception = await Should.ThrowAsync<InvalidOperationException>(() => host.BuildAsync(project.Resource));

        exception.Message.ShouldContain("WithView");
    }

    [Theory]
    [InlineData("rst")]
    [InlineData("exe.northwind.invoicing")]
    [InlineData("exe.northwind.invoicing.dev.nightly")]
    [InlineData("xyz.northwind")]
    [InlineData("rst..invoicing")]
    [InlineData("@acme.billing.rst.invoicing")]
    [InlineData("@acme.billing.default.exe.northwind")]
    [InlineData("@.billing.default.rst.invoicing")]
    public void AnInvalidKey_FailsAtTheCall(string key)
    {
        var host = TestAppHost.Run();
        var project = host.AddProject();

        Should.Throw<ArgumentException>(() => project.WithStorytellerKey(key)).ParamName.ShouldBe("key");
        project.Resource.Annotations.OfType<StorytellerKeyAnnotation>().ShouldBeEmpty();
    }

    [Theory]
    [InlineData("", "invoicing", "dev", "subject")]
    [InlineData("north.wind", "invoicing", "dev", "subject")]
    [InlineData("northwind", " ", "dev", "responsibility")]
    [InlineData("northwind", "invoicing", "d.ev", "context")]
    public void WithExecution_TakesOneSegmentPerName(string subject, string responsibility, string context, string parameterName)
    {
        var host = TestAppHost.Run();
        var project = host.AddProject();

        Should.Throw<ArgumentException>(() => project.WithExecution(subject, responsibility, context)).ParamName.ShouldBe(parameterName);
    }
}
