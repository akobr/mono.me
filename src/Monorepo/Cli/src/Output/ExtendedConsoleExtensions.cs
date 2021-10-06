using McMaster.Extensions.CommandLineUtils;

namespace _42.Monorepo.Cli.Output
{
    public static class ExtendedConsoleExtensions
    {
        public static void WriteLine(this IExtendedConsole console)
        {
            console.Console.WriteLine();
        }
    }
}
