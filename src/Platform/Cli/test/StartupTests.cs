using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using _42.Platform.Cli.Commands;
using _42.Platform.Cli.Commands.KeyValues;
using McMaster.Extensions.CommandLineUtils;
using McMaster.Extensions.CommandLineUtils.Abstractions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Moq;
using Shouldly;
using Xunit;

namespace _42.Platform.Cli.UnitTests;

// McMaster creates a command, and the parents of the selected one, through its public constructor. Every parameter has
// to resolve, also one with a default value; one that does not crashes the command and every command below it, --help
// included. sform story kv crashed this way on its TimeProvider.
public class StartupTests
{
    // What the host and McMaster provide besides the registrations of Startup.
    private static readonly Type[] Provided =
    [
        typeof(IConfiguration),
        typeof(IHostEnvironment),
        typeof(IHostApplicationLifetime),
        typeof(CommandLineApplication),
        typeof(IConsole),
        typeof(CommandLineContext),
    ];

    [Fact]
    public void EveryCommand_ResolvesFromTheServicesOfStartup()
    {
        using var provider = StartupServices().BuildServiceProvider();
        var isService = provider.GetRequiredService<IServiceProviderIsService>();

        var commands = Commands(typeof(PlatformCommand));
        var unresolved = commands
            .Where(command => !IsResolvable(command, type => Provided.Contains(type) || isService.IsService(type)))
            .Select(command => command.FullName)
            .ToList();

        commands.ShouldContain(typeof(KeyValueListCommand));
        unresolved.ShouldBeEmpty();
    }

    [Fact]
    public void TimeProvider_IsTheSystemClock()
    {
        using var provider = StartupServices().BuildServiceProvider();

        provider.GetRequiredService<TimeProvider>().ShouldBeSameAs(TimeProvider.System);
    }

    private static ServiceCollection StartupServices()
    {
        var environment = new Mock<IHostEnvironment>();
        environment.SetupGet(host => host.EnvironmentName).Returns(Environments.Production);

        var services = new ServiceCollection();
        new Startup(environment.Object).ConfigureServices(services, new ConfigurationBuilder().Build());
        return services;
    }

    // The command and every command below it, through [Subcommand].
    private static List<Type> Commands(Type root)
    {
        var commands = new List<Type>();
        var pending = new Queue<Type>([root]);

        while (pending.Count > 0)
        {
            var command = pending.Dequeue();

            if (commands.Contains(command))
            {
                continue;
            }

            commands.Add(command);

            foreach (var subcommand in command.GetCustomAttribute<SubcommandAttribute>()?.Types ?? [])
            {
                pending.Enqueue(subcommand);
            }
        }

        return commands;
    }

    // As ConstructorInjectionConvention decides: a single public constructor has to resolve; of several, one has to.
    private static bool IsResolvable(Type command, Func<Type, bool> isProvided)
    {
        var constructors = command.GetConstructors(BindingFlags.Public | BindingFlags.Instance);
        return constructors.Any(constructor => constructor.GetParameters().All(parameter => isProvided(parameter.ParameterType)));
    }
}
