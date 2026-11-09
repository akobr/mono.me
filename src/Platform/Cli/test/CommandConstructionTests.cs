using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Abstractions;
using System.Linq;
using System.Reflection;
using _42.Platform.Cli.Commands;
using _42.Platform.Cli.Configuration;
using McMaster.Extensions.CommandLineUtils;
using McMaster.Extensions.CommandLineUtils.Abstractions;
using Newtonsoft.Json;
using Shouldly;
using Xunit;

namespace _42.Platform.Cli.UnitTests;

// The model of every command, built the way the command line host builds it: the container of sform's own host
// (Program.CreateHostBuilder with Startup) and McMaster's constructor injection, which ignores the default value of an
// optional parameter and fails on a type the container lacks. Parsing any command line builds the models of the whole
// path, so one such command broke every command below it, their --help included (every `story kv …` exited with 1).
[Collection(nameof(GlobalJsonSettingsCollection))]
public sealed class CommandConstructionTests : IDisposable
{
    private readonly string _home = Path.Combine(Path.GetTempPath(), "sform-commands-" + Guid.NewGuid().ToString("N"));

    [Fact]
    public void EveryCommand_IsBuiltByTheContainerOfTheHost()
    {
        var failures = new List<string>();
        var built = new HashSet<Type>();

        WithHostServices(services =>
        {
            var application = new CommandLineApplication<PlatformCommand>(NullConsole.Singleton);
            application.Conventions.UseDefaultConventions().UseConstructorInjection(services);

            foreach (var (path, command) in Walk(application, CommandNames.SFORM))
            {
                built.Add(((IModelAccessor)command).GetModelType());

                try
                {
                    ((IModelAccessor)command).GetModel().ShouldNotBeNull(path);
                }
                catch (Exception exception)
                {
                    failures.Add($"{path}: {exception.GetBaseException().Message}");
                }
            }
        });

        // Every command of sform is in the tree: each class with [Command].
        var commands = typeof(PlatformCommand).Assembly.GetTypes()
            .Where(type => !type.IsAbstract && type.GetCustomAttribute<CommandAttribute>() is not null)
            .ToHashSet();
        commands.Count.ShouldBeGreaterThan(90);
        built.ShouldBe(commands, ignoreOrder: true);
        failures.ShouldBeEmpty(string.Join(Environment.NewLine, failures));
    }

    [Fact]
    public void TheContainer_HasTheSystemClock()
    {
        WithHostServices(services => services.GetService(typeof(TimeProvider)).ShouldBeSameAs(TimeProvider.System));
    }

    public void Dispose()
    {
        if (Directory.Exists(_home))
        {
            Directory.Delete(_home, recursive: true);
        }
    }

    private static IEnumerable<(string Path, CommandLineApplication Command)> Walk(CommandLineApplication command, string path)
    {
        yield return (path, command);

        foreach (var subcommand in command.Commands)
        {
            foreach (var item in Walk(subcommand, $"{path} {subcommand.Name}"))
            {
                yield return item;
            }
        }
    }

    // Startup.ConfigureApplication sets JsonConvert.DefaultSettings for the process; the collection runs alone, and the
    // settings of the other tests come back afterwards.
    private void WithHostServices(Action<IServiceProvider> test)
    {
        var settings = JsonConvert.DefaultSettings;

        try
        {
            var paths = CliPaths.FromEnvironment(new FileSystem(), name => name == SformVariables.HOME ? _home : null);
            using var host = Program.CreateHostBuilder([], paths).Build();
            test(host.Services);
        }
        finally
        {
            JsonConvert.DefaultSettings = settings;
        }
    }
}

// Tests that change the process-wide state of Newtonsoft.Json run alone, after the parallel ones.
[CollectionDefinition(nameof(GlobalJsonSettingsCollection), DisableParallelization = true)]
public sealed class GlobalJsonSettingsCollection
{
}
