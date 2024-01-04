# Getting Started Guide

> The text-base UI is the best user interface ever made❣️

This library combines multiple .net libraries into one package to simplify the creation of a CLI application. The result is easy to use because it has a nice interface, is IoC-friendly, and uses the `IHostBuilder` principle. 

## Build the host and run it

```csharp
return await new HostBuilder()
    .UseCommandLineApplication<ToolCommand>(args)
    .UseStartup<Startup>()
    .Build()
    .RunCommandLineApplicationAsync();
```

Use the `Startup` class for configuration and building service provider:

```csharp
public class Startup : IStartup
{
    public void ConfigureApplication(IConfigurationBuilder builder)
    {
        // add any configuration here
        // builder.AddJsonFile("appsettings.json", false, false);
    }

    public void ConfigureServices(IServiceCollection services, IConfiguration configuration)
    {
        services.AddCliToolkit(); // register the toolkit (IExtendedConsole, IRenderer, IPrompter, IProgressReporter)
        // register your services here
    }
}
```

## Build commands

```csharp
[Subcommand(typeof(ExampleCommand))]
[Command("tool", Description = "Description in help.")]
public class ToolCommand : BaseCommand
{
    public ToolCommand(IExtendedConsole console)
        : base(console)
    {
        // no operation
    }

    [VersionOption("-v|--version", "", Description = "Display version of this tool.")]
    public bool IsVersionRequested { get; set; }

    public Task<int> OnExecuteAsync()
    {
        Console.WriteLine("Hello world!")
        return Task.FromResult(0);
    }
}
```

> The complete documentation on how to structure commands and sub-commands is on GitHub: https://github.com/natemcmaster/CommandLineUtils 

## Use extended console

For total control, use the `IExtendedConsole` type from the *IoC* container; the system of commands uses injection through the constructor.

If needed, there are more specialized interfaces:

- `IRenderer`
  - `WriteLine(params object[] elements)`
  - `WriteTree<T>(IComposition<T> root, Func<T, IConsoleOutput> nodeRenderFunction)`
  - `WriteTable<T>(IEnumerable<T> rows, Func<T, IEnumerable<Cell>> rowRenderFunction, IEnumerable<IHeaderColumn>? headers = null)`
  - `Write(Document document)`
- `IPrompter`
  - `T Input<T>(InputOptions<T> options)`
  - `bool Confirm(ConfirmOptions options)`
  - `IEnumerable<T> List<T>(ListOptions<T> options)`
  - `T Select<T>(SelectOptions<T> options)`
- `IProgressReporter`
  - `IProgressBar StartProgressBar(string message, ProgressBarOptions? options = null)`

## More examples

A simple demo application is available at: https://github.com/akobr/mono.me/tree/main/src/Libraries/CLI/Toolkit.Example/src

A complex CLI application: https://github.com/akobr/mono.me/tree/main/src/Monorepo/Cli/src

## Adapted libraries

- [Alba.CsConsoleFormat-NoXaml](https://github.com/Athari/CsConsoleFormat) - adds a possibility to draw complex and structured content
- [Colorful.Console](https://github.com/tomakita/Colorful.Console) - makes the console colorful
- [ConsoleTableExt](https://github.com/minhhungit/ConsoleTableExt) - allows the rendering of nice-looking tables
- [McMaster.Extensions.CommandLineUtils](https://github.com/natemcmaster/CommandLineUtils) - brings structure to your command line application
- [Sharprompt](https://github.com/shibayan/Sharprompt) - prompting capabilities
- [ShellProgressBar](https://github.com/Mpdreamz/shellprogressbar) - visualize (concurrent) progress in your console
