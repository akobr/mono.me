using Aspire.Hosting;
using Aspire.Hosting.ApplicationModel;

namespace _42.Platform.Storyteller.AspireHosting.UnitTests;

public sealed class ResourceTests
{
    [Fact]
    public void TheResource_StartsRunning_WithItsTypeAndEndpoint()
    {
        var host = TestAppHost.Run();

        var storyteller = host.Builder.AddStoryteller("storyteller", "https://storyteller.example.com/api");

        var snapshot = storyteller.Resource.Annotations.OfType<ResourceSnapshotAnnotation>().Single().InitialSnapshot;
        snapshot.ResourceType.ShouldBe("Storyteller");
        snapshot.State.ShouldNotBeNull().Text.ShouldBe(KnownResourceStates.Running);
        snapshot.Properties.ShouldContain(property => property.Name == CustomResourceKnownProperties.Source && Equals(property.Value, "https://storyteller.example.com/api"));
    }

    [Fact]
    public void TheDashboard_ShowsTheExpression_OfAParameterEndpoint_NotItsValue()
    {
        var host = TestAppHost.Run().WithParameter("storyteller-endpoint", "https://storyteller.example.com/api");

        var storyteller = host.Builder.AddStoryteller("storyteller", host.Builder.AddParameter("storyteller-endpoint"));

        var snapshot = storyteller.Resource.Annotations.OfType<ResourceSnapshotAnnotation>().Single().InitialSnapshot;
        snapshot.Properties.Single(property => property.Name == CustomResourceKnownProperties.Source).Value.ShouldBe("{storyteller-endpoint.value}");
    }

    [Fact]
    public void TheDashboard_ShowsTheExpression_OfAnEndpointReference()
    {
        var host = TestAppHost.Run();
        var api = host.AddProject("api").WithHttpEndpoint(name: "http");

        var storyteller = host.Builder.AddStoryteller("storyteller", $"{api.GetEndpoint("http")}/api");

        var snapshot = storyteller.Resource.Annotations.OfType<ResourceSnapshotAnnotation>().Single().InitialSnapshot;
        snapshot.Properties.Single(property => property.Name == CustomResourceKnownProperties.Source).Value.ShouldBe("{api.bindings.http.url}/api");
    }

    [Fact]
    public void TheResource_IsExcludedFromTheManifest()
    {
        var host = TestAppHost.Publish();

        var storyteller = host.Builder.AddStoryteller("storyteller", "https://storyteller.example.com/api");

        storyteller.Resource.Annotations.OfType<ManifestPublishingCallbackAnnotation>().ShouldBe([ManifestPublishingCallbackAnnotation.Ignore]);
    }

    [Fact]
    public void TheResource_HasNoConnectionString()
    {
        typeof(IResourceWithConnectionString).IsAssignableFrom(typeof(StorytellerResource)).ShouldBeFalse();
        typeof(IResourceWithEnvironment).IsAssignableFrom(typeof(StorytellerResource)).ShouldBeFalse();
    }

    [Fact]
    public void TheEndpointForms_KeepTheirExpression()
    {
        var host = TestAppHost.Run();
        var parameter = host.Builder.AddParameter("storyteller-endpoint");
        var expression = ReferenceExpression.Create($"{parameter}/api");

        host.Builder.AddStoryteller("literal", "http://localhost:7071/api").Resource.Endpoint.ValueExpression.ShouldBe("http://localhost:7071/api");
        host.Builder.AddStoryteller("parameter", parameter).Resource.Endpoint.ValueExpression.ShouldBe("{storyteller-endpoint.value}");
        host.Builder.AddStoryteller("expression", expression).Resource.Endpoint.ShouldBeSameAs(expression);
    }

    [Theory]
    [InlineData("http://storyteller.example.com/api")]
    [InlineData("ftp://storyteller.example.com/api")]
    [InlineData("storyteller.example.com/api")]
    [InlineData("https://user:secret-password@storyteller.example.com/api")]
    [InlineData("https://storyteller.example.com/api?code=secret-code")]
    [InlineData("https://storyteller.example.com/api#fragment")]
    [InlineData("")]
    public void ALiteralEndpoint_ThatTheSdkRefuses_FailsAtTheCall_WithoutItsValue(string endpoint)
    {
        var host = TestAppHost.Run();

        var exception = Should.Throw<ArgumentException>(() => host.Builder.AddStoryteller("storyteller", endpoint));

        exception.ParamName.ShouldBe("endpoint");
        exception.Message.ShouldNotContain("secret");
        exception.Message.ShouldNotContain("example.com");
        host.Builder.Resources.ShouldBeEmpty();
    }

    // A non-constant interpolated string binds to the handler overload, even when its holes are plain strings; it is
    // still a literal URL.
    [Fact]
    public void AnInterpolatedEndpoint_OfPlainStrings_ThatTheSdkRefuses_FailsAtTheCall()
    {
        var host = TestAppHost.Run();
        var name = "storyteller.example.com";

        var exception = Should.Throw<ArgumentException>(() => host.Builder.AddStoryteller("storyteller", $"http://{name}:7071/api"));

        exception.ParamName.ShouldBe("endpoint");
        exception.Message.ShouldNotContain("example.com");
        host.Builder.Resources.ShouldBeEmpty();
    }

    [Fact]
    public void ALiteralExpression_ThatTheSdkRefuses_FailsAtTheCall()
    {
        var host = TestAppHost.Run();
        var name = "storyteller.example.com";

        Should.Throw<ArgumentException>(() => host.Builder.AddStoryteller("storyteller", ReferenceExpression.Create($"http://{name}/api")))
            .ParamName.ShouldBe("endpoint");
        host.Builder.Resources.ShouldBeEmpty();
    }

    [Fact]
    public void AnInterpolatedEndpoint_OfPlainStrings_ThatTheSdkAllows_IsTaken()
    {
        var host = TestAppHost.Run();
        var name = "storyteller.example.com";

        var storyteller = host.Builder.AddStoryteller("storyteller", $"https://{name}/api");

        storyteller.Resource.Endpoint.ValueProviders.ShouldBeEmpty();
        storyteller.Resource.Endpoint.ValueExpression.ShouldBe("https://storyteller.example.com/api");
    }

    [Theory]
    [InlineData("https://storyteller.example.com/api")]
    [InlineData("http://localhost:7071/api")]
    [InlineData("http://127.0.0.1:7071/api")]
    [InlineData("http://[::1]:7071/api")]
    public void ALiteralEndpoint_ThatTheSdkAllows_IsTaken(string endpoint)
    {
        var host = TestAppHost.Run();

        var storyteller = host.Builder.AddStoryteller("storyteller", endpoint);

        storyteller.Resource.Endpoint.ValueExpression.ShouldBe(endpoint);
    }

    [Fact]
    public void WithView_SetsTheView_OfShortKeys()
    {
        var host = TestAppHost.Run();

        var storyteller = host.Builder.AddStoryteller("storyteller", "https://storyteller.example.com/api").WithView("acme", "billing");

        storyteller.Resource.Organization.ShouldBe("acme");
        storyteller.Resource.Project.ShouldBe("billing");
        storyteller.Resource.View.ShouldBe("default");
    }

    [Theory]
    [InlineData("", "billing", "default", "organization")]
    [InlineData("acme", "bil.ling", "default", "project")]
    [InlineData("acme", "billing", " ", "view")]
    public void WithView_TakesOneSegmentPerName(string organization, string project, string view, string parameterName)
    {
        var host = TestAppHost.Run();
        var storyteller = host.Builder.AddStoryteller("storyteller", "https://storyteller.example.com/api");

        Should.Throw<ArgumentException>(() => storyteller.WithView(organization, project, view)).ParamName.ShouldBe(parameterName);
        storyteller.Resource.View.ShouldBeNull();
    }
}
