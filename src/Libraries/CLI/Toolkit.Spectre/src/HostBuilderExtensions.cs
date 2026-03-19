using System;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace _42.CLI.Toolkit;

public static class HostBuilderExtensions
{
    public static IHostBuilder UseStartup<TStartup>(this IHostBuilder builder)
        where TStartup : class, IStartup
    {
        builder.ConfigureAppConfiguration(ConfigureApplication<TStartup>);
        builder.ConfigureServices(ConfigureServices<TStartup>);
        return builder;
    }

    private static void ConfigureApplication<TStartup>(HostBuilderContext context, IConfigurationBuilder builder)
        where TStartup : class, IStartup
    {
        var services = BuildStartupServices<TStartup>(context);
        var startup = services.GetRequiredService<TStartup>();
        context.Properties[typeof(TStartup)] = startup;
        startup.ConfigureApplication(builder);
    }

    private static void ConfigureServices<TStartup>(HostBuilderContext context, IServiceCollection services)
        where TStartup : class, IStartup
    {
        var startup = (TStartup)context.Properties[typeof(TStartup)];
        startup.ConfigureServices(services, context.Configuration);
    }

    private static IServiceProvider BuildStartupServices<TStartup>(
        HostBuilderContext context)
        where TStartup : class, IStartup
    {
        var startupServices = new ServiceCollection();
        startupServices.AddSingleton<IHostEnvironment>(_ => context.HostingEnvironment);
        startupServices.AddSingleton(_ => context.Configuration);
        startupServices.AddSingleton(_ => context.GetCommandLineContext());
        startupServices.AddSingleton(_ => context.Properties);
        startupServices.AddSingleton(_ => context);
        startupServices.AddSingleton<TStartup>();
        return startupServices.BuildServiceProvider();
    }
}
