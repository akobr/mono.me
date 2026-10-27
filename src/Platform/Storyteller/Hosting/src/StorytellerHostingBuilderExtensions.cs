using _42.Platform.Storyteller.Sdk.Configuring.Logging;
using _42.Platform.Storyteller.Sdk.Hosting;
using _42.Platform.Storyteller.Sdk.Hosting.Container;
using _42.Platform.Storyteller.Sdk.Hosting.Reading;
using _42.Platform.Storyteller.Sdk.Hosting.Subscriptions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

// Next to IStorytellerHostingBuilder, so services.AddStoryteller().AddSubscriptions(…) needs no using.
namespace Microsoft.Extensions.DependencyInjection;

public static class StorytellerHostingBuilderExtensions
{
    // A hosted informer for one responsibility in one context that keeps the resolved document of every subscription in
    // memory: IStorytellerSubscriptions. The responsibility is the one in the chain of the options' Key: its own (rst.R),
    // or the one of its unit, usage, execution or unit of execution; a key without one throws. Without a key, with an
    // invalid option, or for a second call in the container, the call throws: StorytellerConfigurationException for
    // the key and the options, ArgumentException for a name, InvalidOperationException for a second call.
    public static IStorytellerHostingBuilder AddSubscriptions(this IStorytellerHostingBuilder builder, string context, Action<StorytellerSubscriptionOptions>? configure = null)
    {
        return Add(builder, null, context, configure);
    }

    // As above, for a named responsibility; a key whose chain names another responsibility throws.
    public static IStorytellerHostingBuilder AddSubscriptions(this IStorytellerHostingBuilder builder, string responsibility, string context, Action<StorytellerSubscriptionOptions>? configure = null)
    {
        ArgumentNullException.ThrowIfNull(responsibility);

        return Add(builder, responsibility, context, configure);
    }

    // IOptionsMonitor<TOptions>.Get(subject) and IOptionsSnapshot<TOptions>.Get(subject) bind the document of that
    // subject's subscription, or its section, with ConfigurationBinder; IOptionsMonitor<TOptions>.OnChange is raised for
    // the subject on every Added and Changed. Any other name than the default one fails validation, so a worker never
    // runs a tenant on default options by mistake; Configure<TOptions>(…) applies to the default name only. Values
    // shared by all subjects come from ConfigureAll<TOptions>, and as for any configure action of the options, the
    // order of registration decides: the documents bind where this call registers them, so ConfigureAll<TOptions>
    // before it gives defaults that the documents override, ConfigureAll<TOptions> after it overrides the documents,
    // and PostConfigureAll<TOptions> runs after them in any order. Needs AddSubscriptions, in any order: without it the
    // first resolution of the options throws InvalidOperationException. Once per options type.
    public static IStorytellerHostingBuilder AddSubjectOptions<TOptions>(this IStorytellerHostingBuilder builder, string? section = null)
        where TOptions : class
    {
        ArgumentNullException.ThrowIfNull(builder);

        if (section is not null)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(section);
        }

        var services = builder.Services;
        var settings = HostingOf(builder).Settings;

        if (services.Any(descriptor => descriptor.ServiceType == typeof(SubjectOptionsConfigurator<TOptions>)))
        {
            throw new InvalidOperationException($"AddSubjectOptions<{typeof(TOptions).Name}>() was called before; the options of a type bind one section of the documents.");
        }

        services.AddOptions();
        services.AddSingleton(serviceProvider => new SubjectOptionsConfigurator<TOptions>(RequireSubscriptions<TOptions>(serviceProvider), section));
        services.AddSingleton<IConfigureOptions<TOptions>>(serviceProvider => serviceProvider.GetRequiredService<SubjectOptionsConfigurator<TOptions>>());
        services.AddSingleton<IValidateOptions<TOptions>>(serviceProvider => new SubjectOptionsValidator<TOptions>(
            RequireSubscriptions<TOptions>(serviceProvider),
            serviceProvider.GetRequiredService<SubjectOptionsConfigurator<TOptions>>()));

        // Closed over TOptions, so it wins over the framework's open OptionsMonitor<> for this type only.
        services.AddSingleton<IOptionsMonitor<TOptions>>(serviceProvider => new SubjectOptionsMonitor<TOptions>(
            serviceProvider.GetRequiredService<IOptionsFactory<TOptions>>(),
            serviceProvider.GetServices<IOptionsChangeTokenSource<TOptions>>(),
            serviceProvider.GetRequiredService<IOptionsMonitorCache<TOptions>>(),
            RequireSubscriptions<TOptions>(serviceProvider),
            new SwitchableLoggerFactory(settings.LoggerFactory ?? serviceProvider.GetService<ILoggerFactory>()).CreateLogger(SubscriptionsService.LogCategory)));

        return builder;
    }

    private static IStorytellerHostingBuilder Add(IStorytellerHostingBuilder builder, string? responsibility, string context, Action<StorytellerSubscriptionOptions>? configure)
    {
        ArgumentNullException.ThrowIfNull(builder);

        var services = builder.Services;
        var hosting = HostingOf(builder);

        if (services.Any(descriptor => descriptor.ServiceType == typeof(SubscriptionsService)))
        {
            throw new InvalidOperationException("AddSubscriptions was called before: a container holds the subscriptions of one responsibility in one context.");
        }

        var settings = hosting.Settings;
        var subscription = SubscriptionSettings.Create(settings, responsibility, context, configure);

        // By factories, so the container disposes the service, and with it the private documents.
        services.AddSingleton(serviceProvider => new SubscriptionsService(
            subscription,
            settings,
            serviceProvider.GetRequiredService<CatalogReader>(),
            serviceProvider.GetService<ILoggerFactory>()));
        services.AddSingleton<IStorytellerSubscriptions>(serviceProvider => serviceProvider.GetRequiredService<SubscriptionsService>());
        services.AddHostedService(serviceProvider => serviceProvider.GetRequiredService<SubscriptionsService>());
        return builder;
    }

    // The settings services.AddStoryteller(…) read; a builder from elsewhere has none.
    private static HostingSettings HostingOf(IStorytellerHostingBuilder builder)
    {
        return builder.Services.LastOrDefault(descriptor => descriptor.ServiceType == typeof(HostingSettings))?.ImplementationInstance as HostingSettings
            ?? throw new InvalidOperationException("The subscriptions build on Storyteller Hosting: call services.AddStoryteller(…) first and add to the builder it returns.");
    }

    private static SubscriptionsService RequireSubscriptions<TOptions>(IServiceProvider serviceProvider)
    {
        return serviceProvider.GetService<SubscriptionsService>()
            ?? throw new InvalidOperationException(
                $"AddSubjectOptions<{typeof(TOptions).Name}>() binds the documents of the subscriptions, and there are none: call AddSubscriptions(context) or AddSubscriptions(responsibility, context) on the builder of services.AddStoryteller(…) as well.");
    }
}
