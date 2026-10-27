using Aspire.Hosting;
using Aspire.Hosting.ApplicationModel;

namespace _42.Platform.Storyteller.AspireHosting.UnitTests;

public sealed class ReferenceTests
{
    private const string Endpoint = "https://storyteller.example.com/api";

    public enum Destination
    {
        Project,
        Executable,
        Container,
    }

    [Theory]
    [InlineData(Destination.Project)]
    [InlineData(Destination.Executable)]
    [InlineData(Destination.Container)]
    public async Task ALiteralEndpoint_IsPassedAsWritten_ToEveryKindOfDestination(Destination kind)
    {
        var host = TestAppHost.Run();
        var storyteller = host.Builder.AddStoryteller("storyteller", Endpoint);

        var destination = AddDestination(host, kind, storyteller);

        (await host.ReadVariablesAsync(destination)).ShouldBe(new Dictionary<string, string?> { ["STORYTELLER_ENDPOINT"] = Endpoint });
    }

    [Fact]
    public async Task AParameterEndpoint_IsResolvedInRunMode()
    {
        var host = TestAppHost.Run().WithParameter("storyteller-endpoint", Endpoint);
        var storyteller = host.Builder.AddStoryteller("storyteller", host.Builder.AddParameter("storyteller-endpoint"));
        var project = host.AddProject().WithReference(storyteller);

        (await host.ReadVariablesAsync(project.Resource)).ShouldBe(new Dictionary<string, string?> { ["STORYTELLER_ENDPOINT"] = Endpoint });
    }

    [Fact]
    public async Task AParameterEndpoint_IsAReferenceInPublishMode()
    {
        var host = TestAppHost.Publish();
        var storyteller = host.Builder.AddStoryteller("storyteller", host.Builder.AddParameter("storyteller-endpoint"));
        var project = host.AddProject().WithReference(storyteller);

        (await host.ReadVariablesAsync(project.Resource)).ShouldBe(new Dictionary<string, string?> { ["STORYTELLER_ENDPOINT"] = "{storyteller-endpoint.value}" });
    }

    [Fact]
    public async Task AnExpressionEndpoint_IsResolvedInRunMode_AndAnExpressionInPublishMode()
    {
        var run = TestAppHost.Run().WithParameter("storyteller-host", "https://storyteller.example.com");
        var runHost = run.Builder.AddParameter("storyteller-host");
        var runProject = run.AddProject().WithReference(run.Builder.AddStoryteller("storyteller", ReferenceExpression.Create($"{runHost}/api")));

        var publish = TestAppHost.Publish();
        var publishHost = publish.Builder.AddParameter("storyteller-host");
        var publishProject = publish.AddProject().WithReference(publish.Builder.AddStoryteller("storyteller", ReferenceExpression.Create($"{publishHost}/api")));

        (await run.ReadVariablesAsync(runProject.Resource)).ShouldBe(new Dictionary<string, string?> { ["STORYTELLER_ENDPOINT"] = Endpoint });
        (await publish.ReadVariablesAsync(publishProject.Resource)).ShouldBe(new Dictionary<string, string?> { ["STORYTELLER_ENDPOINT"] = "{storyteller-host.value}/api" });
    }

    // In run mode the endpoint of a project is allocated by DCP, which these tests do not start.
    [Fact]
    public async Task TheEndpointOfAProject_IsAnExpressionInPublishMode()
    {
        var host = TestAppHost.Publish();
        var api = host.AddProject("api").WithHttpEndpoint(name: "http");
        var storyteller = host.Builder.AddStoryteller("storyteller", $"{api.GetEndpoint("http")}/api");
        var project = host.AddProject().WithReference(storyteller);

        (await host.ReadVariablesAsync(project.Resource)).ShouldBe(new Dictionary<string, string?> { ["STORYTELLER_ENDPOINT"] = "{api.bindings.http.url}/api" });
    }

    [Fact]
    public async Task AnApiKey_IsResolvedInRunMode()
    {
        var host = TestAppHost.Run().WithParameter("storyteller-api-key", "st_machine_secret");
        var storyteller = host.Builder.AddStoryteller("storyteller", Endpoint)
            .WithApiKey(host.Builder.AddParameter("storyteller-api-key", secret: true));
        var project = host.AddProject().WithReference(storyteller);

        (await host.ReadVariablesAsync(project.Resource)).ShouldBe(new Dictionary<string, string?>
        {
            ["STORYTELLER_ENDPOINT"] = Endpoint,
            ["STORYTELLER_API_KEY"] = "st_machine_secret",
        });
    }

    [Fact]
    public async Task ClientCredentials_AreResolvedInRunMode()
    {
        var host = TestAppHost.Run()
            .WithParameter("storyteller-client-id", "machine-id")
            .WithParameter("storyteller-client-secret", "machine-secret");
        var storyteller = host.Builder.AddStoryteller("storyteller", Endpoint)
            .WithClientCredentials(
                host.Builder.AddParameter("storyteller-client-id"),
                host.Builder.AddParameter("storyteller-client-secret", secret: true),
                "https://login.example.com/oauth2/token",
                "api://storyteller/.default");
        var project = host.AddProject().WithReference(storyteller);

        (await host.ReadVariablesAsync(project.Resource)).ShouldBe(new Dictionary<string, string?>
        {
            ["STORYTELLER_ENDPOINT"] = Endpoint,
            ["STORYTELLER_CLIENT_ID"] = "machine-id",
            ["STORYTELLER_CLIENT_SECRET"] = "machine-secret",
            ["STORYTELLER_TOKEN_URL"] = "https://login.example.com/oauth2/token",
            ["STORYTELLER_TOKEN_SCOPE"] = "api://storyteller/.default",
        });
    }

    [Fact]
    public async Task ClientCredentials_WithoutATokenUrlOrScope_PassNeither()
    {
        var host = TestAppHost.Run()
            .WithParameter("storyteller-client-id", "client_01ABC")
            .WithParameter("storyteller-client-secret", "machine-secret");
        var storyteller = host.Builder.AddStoryteller("storyteller", Endpoint)
            .WithClientCredentials(host.Builder.AddParameter("storyteller-client-id"), host.Builder.AddParameter("storyteller-client-secret", secret: true));
        var project = host.AddProject().WithReference(storyteller);

        (await host.ReadVariablesAsync(project.Resource)).Keys.ShouldBe(
            ["STORYTELLER_ENDPOINT", "STORYTELLER_CLIENT_ID", "STORYTELLER_CLIENT_SECRET"],
            ignoreOrder: true);
    }

    [Fact]
    public async Task AClientCertificate_PassesItsPathAndPassword()
    {
        var host = TestAppHost.Run().WithParameter("storyteller-certificate-password", "pfx-password");
        var storyteller = host.Builder.AddStoryteller("storyteller", Endpoint)
            .WithClientCertificate("certificates/machine.pfx", host.Builder.AddParameter("storyteller-certificate-password", secret: true));
        var project = host.AddProject().WithReference(storyteller);

        (await host.ReadVariablesAsync(project.Resource)).ShouldBe(new Dictionary<string, string?>
        {
            ["STORYTELLER_ENDPOINT"] = Endpoint,
            ["STORYTELLER_CLIENT_CERTIFICATE"] = Path.Combine(TestAppHost.Directory, "certificates", "machine.pfx"),
            ["STORYTELLER_CLIENT_CERTIFICATE_PASSWORD"] = "pfx-password",
        });
    }

    [Fact]
    public async Task AClientCertificate_WithoutAPassword_PassesItsPathOnly()
    {
        var host = TestAppHost.Run();
        var storyteller = host.Builder.AddStoryteller("storyteller", Endpoint).WithClientCertificate("machine.pem");
        var project = host.AddProject().WithReference(storyteller);

        (await host.ReadVariablesAsync(project.Resource)).ShouldBe(new Dictionary<string, string?>
        {
            ["STORYTELLER_ENDPOINT"] = Endpoint,
            ["STORYTELLER_CLIENT_CERTIFICATE"] = Path.Combine(TestAppHost.Directory, "machine.pem"),
        });
    }

    [Fact]
    public async Task AnApiKeyWithAClientCertificate_PassesBoth()
    {
        var host = TestAppHost.Run().WithParameter("storyteller-api-key", "st_machine_secret");
        var storyteller = host.Builder.AddStoryteller("storyteller", Endpoint)
            .WithClientCertificate("machine.pem")
            .WithApiKey(host.Builder.AddParameter("storyteller-api-key", secret: true));
        var project = host.AddProject().WithReference(storyteller);

        (await host.ReadVariablesAsync(project.Resource)).ShouldBe(new Dictionary<string, string?>
        {
            ["STORYTELLER_ENDPOINT"] = Endpoint,
            ["STORYTELLER_API_KEY"] = "st_machine_secret",
            ["STORYTELLER_CLIENT_CERTIFICATE"] = Path.Combine(TestAppHost.Directory, "machine.pem"),
        });
    }

    [Fact]
    public async Task Secrets_AreReferencesInPublishMode()
    {
        var host = TestAppHost.Publish();
        var apiKey = host.Builder.AddStoryteller("with-api-key", Endpoint)
            .WithApiKey(host.Builder.AddParameter("storyteller-api-key", secret: true))
            .WithClientCertificate("machine.pem", host.Builder.AddParameter("storyteller-certificate-password", secret: true));
        var clientCredentials = host.Builder.AddStoryteller("with-client-credentials", Endpoint)
            .WithClientCredentials(host.Builder.AddParameter("storyteller-client-id"), host.Builder.AddParameter("storyteller-client-secret", secret: true));
        var first = host.AddProject("first").WithReference(apiKey);
        var second = host.AddProject("second").WithReference(clientCredentials);

        (await host.ReadVariablesAsync(first.Resource)).ShouldBe(new Dictionary<string, string?>
        {
            ["STORYTELLER_ENDPOINT"] = Endpoint,
            ["STORYTELLER_API_KEY"] = "{storyteller-api-key.value}",
            ["STORYTELLER_CLIENT_CERTIFICATE"] = Path.Combine(TestAppHost.Directory, "machine.pem"),
            ["STORYTELLER_CLIENT_CERTIFICATE_PASSWORD"] = "{storyteller-certificate-password.value}",
        });
        (await host.ReadVariablesAsync(second.Resource)).ShouldBe(new Dictionary<string, string?>
        {
            ["STORYTELLER_ENDPOINT"] = Endpoint,
            ["STORYTELLER_CLIENT_ID"] = "{storyteller-client-id.value}",
            ["STORYTELLER_CLIENT_SECRET"] = "{storyteller-client-secret.value}",
        });
    }

    [Fact]
    public async Task ADestinationWithoutReference_GetsNoStorytellerVariables()
    {
        var host = TestAppHost.Run();
        host.Builder.AddStoryteller("storyteller", Endpoint);
        var project = host.AddProject();

        (await host.ReadVariablesAsync(project.Resource)).ShouldBeEmpty();
    }

    [Fact]
    public void TheReference_IsARelationship_ForTheDashboard()
    {
        var host = TestAppHost.Run();
        var storyteller = host.Builder.AddStoryteller("storyteller", Endpoint);

        var project = host.AddProject().WithReference(storyteller);

        project.Resource.Annotations.OfType<ResourceRelationshipAnnotation>()
            .Where(relationship => ReferenceEquals(relationship.Resource, storyteller.Resource))
            .Select(relationship => relationship.Type)
            .ShouldBe(["Reference"]);
    }

    [Fact]
    public async Task ASecondReference_ReplacesTheFirst()
    {
        var host = TestAppHost.Run().WithParameter("storyteller-api-key", "st_machine_secret");
        var first = host.Builder.AddStoryteller("first", "https://first.example.com/api")
            .WithApiKey(host.Builder.AddParameter("storyteller-api-key", secret: true));
        var second = host.Builder.AddStoryteller("second", "https://second.example.com/api").WithClientCertificate("machine.pem");

        var project = host.AddProject().WithReference(first).WithReference(second);

        (await host.ReadVariablesAsync(project.Resource)).ShouldBe(new Dictionary<string, string?>
        {
            ["STORYTELLER_ENDPOINT"] = "https://second.example.com/api",
            ["STORYTELLER_CLIENT_CERTIFICATE"] = Path.Combine(TestAppHost.Directory, "machine.pem"),
        });
        project.Resource.Annotations.OfType<ResourceRelationshipAnnotation>()
            .Select(relationship => relationship.Resource)
            .ShouldNotContain(first.Resource);
        project.Resource.Annotations.OfType<ResourceRelationshipAnnotation>()
            .Count(relationship => ReferenceEquals(relationship.Resource, second.Resource))
            .ShouldBe(1);
    }

    [Fact]
    public void AContainer_RefusesAResourceWithACertificate()
    {
        var host = TestAppHost.Run();
        var storyteller = host.Builder.AddStoryteller("storyteller", Endpoint).WithClientCertificate("machine.pem");
        var container = host.AddContainer();

        var exception = Should.Throw<InvalidOperationException>(() => container.WithReference(storyteller));

        exception.Message.ShouldContain("'storyteller'");
        exception.Message.ShouldContain("'python-worker'");
        container.Resource.Annotations.OfType<ResourceRelationshipAnnotation>().ShouldBeEmpty();
    }

    [Fact]
    public async Task AContainer_FailsItsStart_WhenTheCertificateIsAddedAfterTheReference()
    {
        var host = TestAppHost.Run();
        var storyteller = host.Builder.AddStoryteller("storyteller", Endpoint);
        var container = host.AddContainer().WithReference(storyteller);
        storyteller.WithClientCertificate("machine.pem");

        var exception = await Should.ThrowAsync<InvalidOperationException>(() => host.BuildAsync(container.Resource));

        exception.Message.ShouldContain("'python-worker'");
    }

    [Fact]
    public async Task AContainer_TakesAnApiKey()
    {
        var host = TestAppHost.Run().WithParameter("storyteller-api-key", "st_machine_secret");
        var storyteller = host.Builder.AddStoryteller("storyteller", Endpoint)
            .WithApiKey(host.Builder.AddParameter("storyteller-api-key", secret: true));
        var container = host.AddContainer().WithReference(storyteller);

        (await host.ReadVariablesAsync(container.Resource)).ShouldBe(new Dictionary<string, string?>
        {
            ["STORYTELLER_ENDPOINT"] = Endpoint,
            ["STORYTELLER_API_KEY"] = "st_machine_secret",
        });
    }

    private static IResource AddDestination(TestAppHost host, Destination kind, IResourceBuilder<StorytellerResource> storyteller)
    {
        return kind switch
        {
            Destination.Project => host.AddProject().WithReference(storyteller).Resource,
            Destination.Executable => host.AddExecutable().WithReference(storyteller).Resource,
            _ => host.AddContainer().WithReference(storyteller).Resource,
        };
    }
}
