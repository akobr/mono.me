using _42.Platform.Storyteller.Sdk.Configuring;
using _42.Platform.Storyteller.Sdk.Configuring.Container;
using _42.Platform.Storyteller.Sdk.Configuring.Documents;
using _42.Platform.Storyteller.Sdk.Hosting.UnitTests.Informer;
using _42.Platform.Storyteller.Sdk.Hosting.UnitTests.Subscriptions;
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

    [Theory]
    [InlineData("rst.invoicing")]
    [InlineData("unt.invoicing.pdf")]
    [InlineData("usg.northwind.invoicing")]
    [InlineData("exe.northwind.invoicing.prod")]
    [InlineData("uxe.northwind.invoicing.prod.pdf")]
    public void AddSubscriptions_TakesTheResponsibility_FromTheChainOfTheKey(string annotationKey)
    {
        _fixture.Environment["STORYTELLER_KEY"] = $"{HostingFixture.Scope}.{annotationKey}";
        var services = new ServiceCollection();

        services.AddStoryteller(_fixture.Configure()).AddSubscriptions("prod");

        using var provider = _fixture.Track(services.BuildServiceProvider());
        var subscriptions = provider.GetRequiredService<IStorytellerSubscriptions>();
        subscriptions.Responsibility.ShouldBe("invoicing");
        subscriptions.Context.ShouldBe("prod");
    }

    [Theory]
    [InlineData("sbt.northwind")]
    [InlineData("cnt.northwind.prod")]
    public void AddSubscriptions_WithoutAResponsibility_ThrowsForAKeyWithoutOne(string annotationKey)
    {
        _fixture.Environment["STORYTELLER_KEY"] = $"{HostingFixture.Scope}.{annotationKey}";
        var builder = new ServiceCollection().AddStoryteller(_fixture.Configure());

        var exception = Should.Throw<StorytellerConfigurationException>(() => builder.AddSubscriptions("prod"));

        exception.Message.ShouldStartWith($"The key {HostingFixture.Scope}.{annotationKey} names no responsibility to take the subscriptions of: set STORYTELLER_KEY");
        exception.Key.ShouldBe($"{HostingFixture.Scope}.{annotationKey}");
        builder.Services.ShouldNotContain(descriptor => descriptor.ServiceType == typeof(IStorytellerSubscriptions));
    }

    [Fact]
    public void AddSubscriptions_WithAResponsibility_TakesOnlyTheViewFromAKeyWithoutOne()
    {
        _fixture.Environment["STORYTELLER_KEY"] = $"{HostingFixture.Scope}.sbt.northwind";
        var services = new ServiceCollection();

        services.AddStoryteller(_fixture.Configure()).AddSubscriptions("invoicing", "prod");

        using var provider = _fixture.Track(services.BuildServiceProvider());
        provider.GetRequiredService<IStorytellerSubscriptions>().Responsibility.ShouldBe("invoicing");
    }

    [Fact]
    public void AddSubscriptions_WithTheResponsibilityOfTheKey_IsAccepted()
    {
        var services = new ServiceCollection();

        services.AddStoryteller(_fixture.Configure()).AddSubscriptions("invoicing", "dev");

        using var provider = _fixture.Track(services.BuildServiceProvider());
        var subscriptions = provider.GetRequiredService<IStorytellerSubscriptions>();
        subscriptions.Responsibility.ShouldBe("invoicing");
        subscriptions.Context.ShouldBe("dev");
    }

    [Fact]
    public void AddSubscriptions_WithAResponsibilityThatContradictsTheKey_Throws()
    {
        var builder = new ServiceCollection().AddStoryteller(_fixture.Configure());

        var exception = Should.Throw<StorytellerConfigurationException>(() => builder.AddSubscriptions("shipping", "prod"));

        exception.Message.ShouldBe(
            $"AddSubscriptions names the responsibility shipping, but the key {HostingFixture.Scope}.{HostingFixture.ExecutionKey} belongs to the responsibility invoicing: a process watches the subscriptions of its own responsibility. Name invoicing, or set STORYTELLER_KEY to a key of shipping.");
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void AddSubscriptions_WithoutAKey_Throws_NamingTheVariableAndTheOption(bool withResponsibility)
    {
        _fixture.Environment.Remove("STORYTELLER_KEY");
        var builder = new ServiceCollection().AddStoryteller(_fixture.Configure());

        var exception = Should.Throw<StorytellerConfigurationException>(() => withResponsibility
            ? builder.AddSubscriptions("invoicing", "prod")
            : builder.AddSubscriptions("prod"));

        exception.Message.ShouldContain("STORYTELLER_KEY");
        exception.Message.ShouldContain("StorytellerConfigurationOptions.Key");
    }

    [Theory]
    [InlineData("", "prod", "responsibility")]
    [InlineData("in.voicing", "prod", "responsibility")]
    [InlineData("invoicing", " ", "context")]
    [InlineData("invoicing", "pr.od", "context")]
    public void AddSubscriptions_RefusesANameThatIsNotOneSegment(string responsibility, string context, string parameter)
    {
        var builder = new ServiceCollection().AddStoryteller(_fixture.Configure());

        Should.Throw<ArgumentException>(() => builder.AddSubscriptions(responsibility, context)).ParamName.ShouldBe(parameter);
    }

    [Fact]
    public void AddSubscriptions_Twice_Throws()
    {
        var builder = new ServiceCollection().AddStoryteller(_fixture.Configure()).AddSubscriptions("prod");

        Should.Throw<InvalidOperationException>(() => builder.AddSubscriptions("invoicing", "dev"))
            .Message.ShouldContain("AddSubscriptions was called before");
    }

    [Fact]
    public void AddSubscriptions_RegistersOneServiceForTheSubscriptionsAndTheHost()
    {
        var services = new ServiceCollection();
        services.AddStoryteller(_fixture.Configure()).AddSubscriptions("prod");

        using var provider = _fixture.Track(services.BuildServiceProvider());

        var subscriptions = provider.GetRequiredService<IStorytellerSubscriptions>();
        var hosted = services.Where(descriptor => descriptor.ServiceType == typeof(IHostedService) && descriptor.ImplementationFactory is not null).ShouldHaveSingleItem();
        hosted.ImplementationFactory!(provider).ShouldBeSameAs(subscriptions);
        services.Count(descriptor => descriptor.ServiceType == typeof(IStorytellerSubscriptions)).ShouldBe(1);
    }

    [Theory]
    [InlineData(nameof(StorytellerSubscriptionOptions.PollInterval), -1)]
    [InlineData(nameof(StorytellerSubscriptionOptions.PollInterval), 500)]
    [InlineData(nameof(StorytellerSubscriptionOptions.PollInterval), 46 * 24 * 3600 * 1000L)]
    [InlineData(nameof(StorytellerSubscriptionOptions.MaxConcurrentReads), 0)]
    [InlineData(nameof(StorytellerSubscriptionOptions.MaxConcurrentReads), 65)]
    [InlineData(nameof(StorytellerSubscriptionOptions.StartTimeout), 0)]
    [InlineData(nameof(StorytellerSubscriptionOptions.Mode), 7)]
    public void AddSubscriptions_ValidatesItsOptions(string property, long value)
    {
        var builder = new ServiceCollection().AddStoryteller(_fixture.Configure());

        var exception = Should.Throw<StorytellerConfigurationException>(() => builder.AddSubscriptions("prod", options =>
        {
            switch (property)
            {
                case nameof(options.PollInterval):
                    options.PollInterval = TimeSpan.FromMilliseconds(value);
                    break;
                case nameof(options.MaxConcurrentReads):
                    options.MaxConcurrentReads = (int)value;
                    break;
                case nameof(options.StartTimeout):
                    options.StartTimeout = TimeSpan.FromMilliseconds(value);
                    break;
                default:
                    options.Mode = (StorytellerReadMode)value;
                    break;
            }
        }));

        exception.Message.ShouldStartWith($"StorytellerSubscriptionOptions.{property} ");
        builder.Services.ShouldNotContain(descriptor => descriptor.ServiceType == typeof(IStorytellerSubscriptions));
    }

    [Fact]
    public void AddSubjectOptions_WithoutAddSubscriptions_ThrowsAtTheFirstResolution_NamingTheMissingCall()
    {
        var services = new ServiceCollection();
        services.AddStoryteller(_fixture.Configure()).AddSubjectOptions<TenantOptions>();
        using var provider = _fixture.Track(services.BuildServiceProvider());

        Should.Throw<InvalidOperationException>(() => provider.GetRequiredService<IOptionsMonitor<TenantOptions>>())
            .Message.ShouldBe(
                "AddSubjectOptions<TenantOptions>() binds the documents of the subscriptions, and there are none: call AddSubscriptions(context) or AddSubscriptions(responsibility, context) on the builder of services.AddStoryteller(…) as well.");
    }

    [Fact]
    public void AddSubjectOptions_BeforeAddSubscriptions_IsFine_ButTwiceForATypeThrows()
    {
        var services = new ServiceCollection();
        var builder = services.AddStoryteller(_fixture.Configure()).AddSubjectOptions<TenantOptions>().AddSubscriptions("prod");

        Should.Throw<InvalidOperationException>(() => builder.AddSubjectOptions<TenantOptions>("App"));
        using var provider = _fixture.Track(services.BuildServiceProvider());
        provider.GetRequiredService<IOptionsMonitor<TenantOptions>>().GetType().Name.ShouldStartWith("SubjectOptionsMonitor");
    }

    [Fact]
    public void AddSubjectOptions_LeavesTheMonitorsOfOtherTypesToTheFramework()
    {
        var services = new ServiceCollection();
        services.AddStoryteller(_fixture.Configure()).AddSubscriptions("prod").AddSubjectOptions<TenantOptions>();
        using var provider = _fixture.Track(services.BuildServiceProvider());

        provider.GetRequiredService<IOptionsMonitor<StorytellerExecutionContextOptions>>().ShouldBeOfType<OptionsMonitor<StorytellerExecutionContextOptions>>();
    }

    [Fact]
    public async Task WhoAmI_TheCatalog_TheDocumentsAndTheSubscriptions_ShareOneBoundCredential()
    {
        var credential = TestCredential.Bearer("token-1");
        var catalog = new FakeCatalog(_fixture);
        catalog.Subscribe("northwind");
        catalog.Put("sbt.northwind");
        catalog.Put("cnt.northwind.prod");
        _fixture.RouteConfiguration(HostingFixture.ExecutionKey);
        await using var harness = new SubscriptionsHarness(_fixture, options: options => options.Credential = credential);

        await harness.Services.GetRequiredService<IExecutionContext>().GetAsync();
        await harness.Services.GetRequiredService<ISubscriptionCatalog>().GetExecutionAsync(HostingFixture.ExecutionKey);
        await harness.Services.GetRequiredService<IStorytellerDocuments>().GetAsync(HostingFixture.ExecutionKey);
        await harness.StartAndPollAsync();

        harness.Subscriptions.Find("northwind").ShouldNotBeNull();
        credential.Bindings.ShouldHaveSingleItem();
        _fixture.Http.Requests.ShouldAllBe(request => request.Authorization == "Bearer token-1");
    }

    public void Dispose()
    {
        _fixture.Dispose();
    }

    public sealed class TenantOptions
    {
        public string? Name { get; set; }
    }
}
