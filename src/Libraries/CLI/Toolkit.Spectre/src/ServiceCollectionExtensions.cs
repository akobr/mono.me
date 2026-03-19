using _42.CLI.Toolkit.Output;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Spectre.Console;

namespace _42.CLI.Toolkit;

public static class ServiceCollectionExtensions
{
    /// <summary>Registers the Spectre.Console-powered CLI toolkit services.</summary>
    public static IServiceCollection AddSpectreCliToolkit(this IServiceCollection @this)
    {
        @this.TryAddSingleton<IAnsiConsole>(_ => Spectre.Console.AnsiConsole.Console);
        @this.TryAddSingleton<IExtendedConsole, SpectreExtendedConsole>();
        @this.TryAddSingleton<IRenderer>(provider => provider.GetRequiredService<IExtendedConsole>());
        @this.TryAddSingleton<IPrompter>(provider => provider.GetRequiredService<IExtendedConsole>());
        @this.TryAddSingleton<IProgressReporter>(provider => provider.GetRequiredService<IExtendedConsole>());
        return @this;
    }

    /// <summary>Alias for <see cref="AddSpectreCliToolkit"/> for drop-in compatibility with the original Toolkit.</summary>
    public static IServiceCollection AddCliToolkit(this IServiceCollection @this)
        => AddSpectreCliToolkit(@this);
}
