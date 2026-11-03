using _42.Platform.Storyteller.Sdk.Credentials;
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

        (await host.ReadVariablesAsync(destination)).ShouldBeVariables(new Dictionary<string, string?> { ["STORYTELLER_ENDPOINT"] = Endpoint });
    }

    [Fact]
    public async Task AParameterEndpoint_IsResolvedInRunMode()
    {
        var host = TestAppHost.Run().WithParameter("storyteller-endpoint", Endpoint);
        var storyteller = host.Builder.AddStoryteller("storyteller", host.Builder.AddParameter("storyteller-endpoint"));
        var project = host.AddProject().WithReference(storyteller);

        (await host.ReadVariablesAsync(project.Resource)).ShouldBeVariables(new Dictionary<string, string?> { ["STORYTELLER_ENDPOINT"] = Endpoint });
    }

    [Fact]
    public async Task AParameterEndpoint_IsAReferenceInPublishMode()
    {
        var host = TestAppHost.Publish();
        var storyteller = host.Builder.AddStoryteller("storyteller", host.Builder.AddParameter("storyteller-endpoint"));
        var project = host.AddProject().WithReference(storyteller);

        (await host.ReadVariablesAsync(project.Resource)).ShouldBeVariables(new Dictionary<string, string?> { ["STORYTELLER_ENDPOINT"] = "{storyteller-endpoint.value}" });
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

        (await run.ReadVariablesAsync(runProject.Resource)).ShouldBeVariables(new Dictionary<string, string?> { ["STORYTELLER_ENDPOINT"] = Endpoint });
        (await publish.ReadVariablesAsync(publishProject.Resource)).ShouldBeVariables(new Dictionary<string, string?> { ["STORYTELLER_ENDPOINT"] = "{storyteller-host.value}/api" });
    }

    // In run mode the endpoint of a project is allocated by DCP, which these tests do not start.
    [Fact]
    public async Task TheEndpointOfAProject_IsAnExpressionInPublishMode()
    {
        var host = TestAppHost.Publish();
        var api = host.AddProject("api").WithHttpEndpoint(name: "http");
        var storyteller = host.Builder.AddStoryteller("storyteller", $"{api.GetEndpoint("http")}/api");
        var project = host.AddProject().WithReference(storyteller);

        (await host.ReadVariablesAsync(project.Resource)).ShouldBeVariables(new Dictionary<string, string?> { ["STORYTELLER_ENDPOINT"] = "{api.bindings.http.url}/api" });
    }

    [Fact]
    public async Task AnApiKey_IsResolvedInRunMode()
    {
        var host = TestAppHost.Run().WithParameter("storyteller-api-key", "st_machine_secret");
        var storyteller = host.Builder.AddStoryteller("storyteller", Endpoint)
            .WithApiKey(host.Builder.AddParameter("storyteller-api-key", secret: true));
        var project = host.AddProject().WithReference(storyteller);

        (await host.ReadVariablesAsync(project.Resource)).ShouldBeVariables(new Dictionary<string, string?>
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

        (await host.ReadVariablesAsync(project.Resource)).ShouldBeVariables(new Dictionary<string, string?>
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

        (await host.ReadVariablesAsync(project.Resource)).ShouldBeVariables(new Dictionary<string, string?>
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

        (await host.ReadVariablesAsync(project.Resource)).ShouldBeVariables(new Dictionary<string, string?>
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

        (await host.ReadVariablesAsync(project.Resource)).ShouldBeVariables(new Dictionary<string, string?>
        {
            ["STORYTELLER_ENDPOINT"] = Endpoint,
            ["STORYTELLER_API_KEY"] = "st_machine_secret",
            ["STORYTELLER_CLIENT_CERTIFICATE"] = Path.Combine(TestAppHost.Directory, "machine.pem"),
        });
    }

    // A published process inherits nothing from the AppHost, so no name is masked.
    [Fact]
    public async Task Secrets_AreReferencesInPublishMode()
    {
        var host = TestAppHost.Publish();
        var apiKey = host.Builder.AddStoryteller("with-api-key", Endpoint)
            .WithApiKey(host.Builder.AddParameter("storyteller-api-key", secret: true));
        var clientCredentials = host.Builder.AddStoryteller("with-client-credentials", Endpoint)
            .WithClientCredentials(host.Builder.AddParameter("storyteller-client-id"), host.Builder.AddParameter("storyteller-client-secret", secret: true));
        var first = host.AddProject("first").WithReference(apiKey);
        var second = host.AddProject("second").WithReference(clientCredentials);

        (await host.ReadAllVariablesAsync(first.Resource)).ShouldBeVariables(new Dictionary<string, string?>
        {
            ["STORYTELLER_ENDPOINT"] = Endpoint,
            ["STORYTELLER_API_KEY"] = "{storyteller-api-key.value}",
        });
        (await host.ReadAllVariablesAsync(second.Resource)).ShouldBeVariables(new Dictionary<string, string?>
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

        (await host.ReadAllVariablesAsync(project.Resource)).ShouldBeEmpty();
    }

    // DCP starts a project or an executable with the AppHost's environment, so every other name the SDK reads is
    // written empty, which every SDK reads as unset.
    [Theory]
    [InlineData(Destination.Project)]
    [InlineData(Destination.Executable)]
    public async Task InRunMode_EveryOtherNameOfTheSdk_IsWrittenEmpty(Destination kind)
    {
        var host = TestAppHost.Run().WithParameter("storyteller-api-key", "st_machine_secret");
        var storyteller = host.Builder.AddStoryteller("storyteller", Endpoint)
            .WithApiKey(host.Builder.AddParameter("storyteller-api-key", secret: true));

        var variables = await host.ReadAllVariablesAsync(AddDestination(host, kind, storyteller));

        variables.Keys.ShouldBe([StorytellerVariables.ENDPOINT, StorytellerVariables.API_URL, StorytellerVariables.KEY, .. StorytellerVariables.Credentials], ignoreOrder: true);
        variables.Where(variable => variable.Value != string.Empty).ToDictionary().ShouldBeVariables(new Dictionary<string, string?>
        {
            ["STORYTELLER_ENDPOINT"] = Endpoint,
            ["STORYTELLER_API_KEY"] = "st_machine_secret",
        });
    }

    // A STORYTELLER_* left in the AppHost's environment for sform reaches the process too; DCP lets the variables of the
    // resource win, and the masks keep the inherited ones from making another credential, endpoint or key.
    [Fact]
    public async Task AVariableInheritedFromTheAppHost_DoesNotMixWithTheReferencedMachine()
    {
        var host = TestAppHost.Run().WithParameter("storyteller-api-key", "st_machine_secret");
        var storyteller = host.Builder.AddStoryteller("storyteller", Endpoint)
            .WithApiKey(host.Builder.AddParameter("storyteller-api-key", secret: true));
        var project = host.AddProject().WithReference(storyteller);

        // The AppHost's environment, under the variables of the resource.
        var environment = new Dictionary<string, string?>
        {
            [StorytellerVariables.API_URL] = "https://other.example.com/api",
            [StorytellerVariables.KEY] = "@house.main.default.rst.other",
            [StorytellerVariables.CLIENT_CERTIFICATE] = "inherited.pfx",
            [StorytellerVariables.CERTIFICATE_FILE] = "inherited-deprecated.pfx",
            [StorytellerVariables.TOKEN_FILE] = "inherited.token",
        };

        foreach (var variable in await host.ReadAllVariablesAsync(project.Resource))
        {
            environment[variable.Key] = variable.Value;
        }

        var credential = StorytellerCredentialVariables.Read(name => environment.GetValueOrDefault(name)).ShouldNotBeNull();
        credential.Kind.ShouldBe(StorytellerCredentialKind.ApiKey);
        credential.Names.ShouldBe([StorytellerVariables.API_KEY]);
        new StorytellerVariableReader(name => environment.GetValueOrDefault(name))
            .ReadWithFallback(StorytellerVariables.ENDPOINT, StorytellerVariables.API_URL, out _)
            .ShouldBe(Endpoint);
        new StorytellerVariableReader(name => environment.GetValueOrDefault(name)).Read(StorytellerVariables.KEY).ShouldBeNull();
    }

    // A variable another callback wrote is not masked, before or after WithReference.
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task AVariableOfTheDestination_IsNotMasked_InEitherOrder(bool isFirst)
    {
        var host = TestAppHost.Run();
        var storyteller = host.Builder.AddStoryteller("storyteller", Endpoint);
        var project = host.AddProject();

        if (isFirst)
        {
            project.WithEnvironment("STORYTELLER_TOKEN_SCOPE", "api://storyteller/.default").WithReference(storyteller);
        }
        else
        {
            project.WithReference(storyteller).WithEnvironment("STORYTELLER_TOKEN_SCOPE", "api://storyteller/.default");
        }

        (await host.ReadAllVariablesAsync(project.Resource))["STORYTELLER_TOKEN_SCOPE"].ShouldBe("api://storyteller/.default");
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

        (await host.ReadVariablesAsync(project.Resource)).ShouldBeVariables(new Dictionary<string, string?>
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

    // Docker starts a container with the variables of its resource only, so no name is masked.
    [Fact]
    public async Task AContainer_TakesAnApiKey_WithoutEmptyNames()
    {
        var host = TestAppHost.Run().WithParameter("storyteller-api-key", "st_machine_secret");
        var storyteller = host.Builder.AddStoryteller("storyteller", Endpoint)
            .WithApiKey(host.Builder.AddParameter("storyteller-api-key", secret: true));
        var container = host.AddContainer().WithReference(storyteller);

        (await host.ReadAllVariablesAsync(container.Resource)).ShouldBeVariables(new Dictionary<string, string?>
        {
            ["STORYTELLER_ENDPOINT"] = Endpoint,
            ["STORYTELLER_API_KEY"] = "st_machine_secret",
        });
    }

    // A deployed process runs elsewhere, where the AppHost's certificate file is not.
    [Fact]
    public void InPublishMode_AResourceWithACertificate_IsRefused()
    {
        var host = TestAppHost.Publish();
        var storyteller = host.Builder.AddStoryteller("storyteller", Endpoint).WithClientCertificate("machine.pem");
        var project = host.AddProject();

        var exception = Should.Throw<InvalidOperationException>(() => project.WithReference(storyteller));

        exception.Message.ShouldContain("'storyteller'");
        exception.Message.ShouldContain("'worker'");
        exception.Message.ShouldContain("run mode only");
        exception.Message.ShouldNotContain("machine.pem");
        project.Resource.Annotations.OfType<ResourceRelationshipAnnotation>().ShouldBeEmpty();
    }

    [Fact]
    public async Task InPublishMode_ACertificateAddedAfterTheReference_FailsThePublish()
    {
        var host = TestAppHost.Publish();
        var storyteller = host.Builder.AddStoryteller("storyteller", Endpoint);
        var project = host.AddProject().WithReference(storyteller);
        storyteller.WithClientCertificate("machine.pem");

        var exception = await Should.ThrowAsync<InvalidOperationException>(() => host.BuildAsync(project.Resource));

        exception.Message.ShouldContain("'worker'");
        exception.Message.ShouldContain("run mode only");
    }

    // Aspire rewrites an endpoint reference for a container, not a literal URL; localhost in a container is the
    // container itself.
    [Theory]
    [InlineData("http://localhost:7071/api")]
    [InlineData("http://127.0.0.1:7071/api")]
    [InlineData("http://[::1]:7071/api")]
    [InlineData("https://localhost:7443/api")]
    public void AContainer_RefusesALiteralLoopbackEndpoint(string endpoint)
    {
        var host = TestAppHost.Run();
        var storyteller = host.Builder.AddStoryteller("storyteller", endpoint);
        var container = host.AddContainer();

        var exception = Should.Throw<InvalidOperationException>(() => container.WithReference(storyteller));

        exception.Message.ShouldContain("'storyteller'");
        exception.Message.ShouldContain("'python-worker'");
        exception.Message.ShouldNotContain(endpoint);
        container.Resource.Annotations.OfType<ResourceRelationshipAnnotation>().ShouldBeEmpty();
    }

    [Fact]
    public void InPublishMode_ALiteralLoopbackEndpoint_IsRefused()
    {
        var host = TestAppHost.Publish();
        var storyteller = host.Builder.AddStoryteller("storyteller", "http://localhost:7071/api");
        var executable = host.AddExecutable();

        var exception = Should.Throw<InvalidOperationException>(() => executable.WithReference(storyteller));

        exception.Message.ShouldContain("'storyteller'");
        exception.Message.ShouldContain("'node-worker'");
        exception.Message.ShouldContain("once it is deployed");
    }

    [Theory]
    [InlineData(Destination.Project)]
    [InlineData(Destination.Executable)]
    public async Task InRunMode_AProjectOrAnExecutable_TakesALiteralLoopbackEndpoint(Destination kind)
    {
        var host = TestAppHost.Run();
        var storyteller = host.Builder.AddStoryteller("storyteller", "http://localhost:7071/api");

        var destination = AddDestination(host, kind, storyteller);

        (await host.ReadVariablesAsync(destination)).ShouldBeVariables(new Dictionary<string, string?> { ["STORYTELLER_ENDPOINT"] = "http://localhost:7071/api" });
    }

    // A parameter is no literal: Aspire resolves it when the container starts.
    [Fact]
    public async Task AContainer_TakesAParameterEndpoint()
    {
        var host = TestAppHost.Run().WithParameter("storyteller-endpoint", Endpoint);
        var storyteller = host.Builder.AddStoryteller("storyteller", host.Builder.AddParameter("storyteller-endpoint"));
        var container = host.AddContainer().WithReference(storyteller);

        (await host.ReadAllVariablesAsync(container.Resource)).ShouldBeVariables(new Dictionary<string, string?> { ["STORYTELLER_ENDPOINT"] = Endpoint });
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
