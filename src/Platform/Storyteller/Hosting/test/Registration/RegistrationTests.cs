using _42.Platform.Storyteller.Sdk.Configuring;
using _42.Platform.Storyteller.Sdk.Configuring.Container;
using _42.Platform.Storyteller.Sdk.Configuring.Documents;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;

namespace _42.Platform.Storyteller.Sdk.Hosting.UnitTests.Registration;

public sealed class RegistrationTests : IDisposable
{
    private readonly HostingFixture _fixture = new();

    [Theory]
    [InlineData("STORYTELLER_ENDPOINT", "ftp://storyteller.example.com/api")]
    [InlineData("STORYTELLER_KEY", "@acme.billing")]
    public void TheSettings_AreReadAtRegistration(string variable, string value)
    {
        _fixture.Environment[variable] = value;
        var services = new ServiceCollection();

        var exception = Should.Throw<StorytellerConfigurationException>(() => services.AddStoryteller(_fixture.Configure()));

        exception.Message.ShouldContain(variable);
        services.ShouldBeEmpty();
    }

    [Fact]
    public async Task TheKey_IsOptionalAtRegistration_WhoAmINeedsIt_AndFullKeysAreReadWithoutIt()
    {
        var fullKey = $"{HostingFixture.Scope}.{HostingFixture.ExecutionKey}";
        _fixture.Environment.Remove("STORYTELLER_KEY");
        _fixture.RouteAnnotation(HostingFixture.ExecutionKey);
        _fixture.RouteConfiguration(HostingFixture.ExecutionKey);

        using var provider = _fixture.BuildServices();

        var whoAmI = await Should.ThrowAsync<StorytellerConfigurationException>(() => provider.GetRequiredService<IExecutionContext>().GetAsync());
        whoAmI.Message.ShouldContain("STORYTELLER_KEY");
        (await provider.GetRequiredService<ISubscriptionCatalog>().GetExecutionAsync(fullKey)).ShouldNotBeNull().Key.ShouldBe(fullKey);
        (await provider.GetRequiredService<IStorytellerDocuments>().GetAsync(fullKey)).Key.ShouldBe(fullKey);
    }

    [Fact]
    public void WhoAmI_TheCatalogAndTheDocuments_AreRegisteredOnce_WithTheProvidersRefresherAndLogging()
    {
        var services = new ServiceCollection();

        var builder = services.AddStoryteller(_fixture.Configure());
        services.AddStoryteller(_fixture.Configure());

        builder.Services.ShouldBeSameAs(services);
        services.Count(descriptor => descriptor.ServiceType == typeof(IExecutionContext)).ShouldBe(1);
        services.Count(descriptor => descriptor.ServiceType == typeof(ISubscriptionCatalog)).ShouldBe(1);
        services.Count(descriptor => descriptor.ServiceType == typeof(IStorytellerDocuments)).ShouldBe(1);
        services.Count(descriptor => descriptor.ServiceType == typeof(IStorytellerConfigurationRefresherProvider)).ShouldBe(1);
        services.Count(descriptor => descriptor.ServiceType == typeof(IHostedService) && descriptor.ImplementationType == typeof(StorytellerLoggingHandover)).ShouldBe(1);
    }

    [Fact]
    public void ALaterRegistration_DoesNotReadItsOptions()
    {
        var services = new ServiceCollection();
        var calls = 0;

        services.AddStoryteller(_fixture.Configure());
        services.AddStoryteller(_ => calls++);

        calls.ShouldBe(0);
    }

    [Fact]
    public void DocumentsRegisteredBefore_AreKept()
    {
        var services = new ServiceCollection();
        services.AddStorytellerDocuments(_fixture.Configure());
        var registered = services.Single(descriptor => descriptor.ServiceType == typeof(IStorytellerDocuments));

        services.AddStoryteller(_fixture.Configure());

        services.Single(descriptor => descriptor.ServiceType == typeof(IStorytellerDocuments)).ShouldBeSameAs(registered);
    }

    [Fact]
    public async Task WhoAmI_TheCatalogAndTheDocuments_ShareOneBoundCredential()
    {
        var credential = TestCredential.Bearer("token-1");
        _fixture.RouteExecution();
        _fixture.RouteConfiguration(HostingFixture.ExecutionKey);
        using var provider = _fixture.BuildServices(options => options.Credential = credential);

        await provider.GetRequiredService<IExecutionContext>().GetAsync();
        await provider.GetRequiredService<ISubscriptionCatalog>().GetExecutionAsync(HostingFixture.ExecutionKey);
        await provider.GetRequiredService<IStorytellerDocuments>().GetAsync(HostingFixture.ExecutionKey);

        var binding = credential.Bindings.ShouldHaveSingleItem();
        binding.Organization.ShouldBe("acme");
        binding.Project.ShouldBe("billing");
        _fixture.Http.Requests.Count.ShouldBe(7);
        _fixture.Http.Requests.ShouldAllBe(request => request.Authorization == "Bearer token-1");
    }

    [Fact]
    public void ANegativeRefreshInterval_FailsTheResolution()
    {
        using var provider = _fixture.BuildServices(services: services => services.Configure<StorytellerExecutionContextOptions>(options => options.RefreshInterval = TimeSpan.FromSeconds(-5)));

        Should.Throw<OptionsValidationException>(() => provider.GetRequiredService<IExecutionContext>())
            .Message.ShouldContain("RefreshInterval");
    }

    [Fact]
    public async Task TheHostApplicationBuilder_AddsTheProviderAndHosting_InOneCall()
    {
        _fixture.RouteExecution();
        _fixture.RouteConfiguration(HostingFixture.ExecutionKey);
        var builder = Host.CreateEmptyApplicationBuilder(new HostApplicationBuilderSettings());

        var hosting = builder.AddStoryteller(_fixture.Configure());

        hosting.Services.ShouldBeSameAs(builder.Services);
        builder.Configuration["App:Name"].ShouldBe("northwind");

        using var host = _fixture.Track(builder.Build());
        host.Services.GetRequiredService<IStorytellerConfigurationRefresher>().ActiveKey.ShouldBe($"{HostingFixture.Scope}.{HostingFixture.ExecutionKey}");
        var context = await host.Services.GetRequiredService<IExecutionContext>().GetAsync();
        context.Execution.ShouldNotBeNull().SubjectName.ShouldBe("northwind");
        host.Services.GetRequiredService<IStorytellerDocuments>().ShouldBeOfType<StorytellerDocuments>();
    }

    [Fact]
    public void TheHostApplicationBuilder_RequiresAKey()
    {
        _fixture.Environment.Remove("STORYTELLER_KEY");
        var builder = Host.CreateEmptyApplicationBuilder(new HostApplicationBuilderSettings());

        Should.Throw<StorytellerConfigurationException>(() => builder.AddStoryteller(_fixture.Configure()))
            .Message.ShouldContain("STORYTELLER_KEY");
    }

    public void Dispose()
    {
        _fixture.Dispose();
    }
}
