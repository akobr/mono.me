using System;
using McMaster.Extensions.CommandLineUtils;

namespace _42.Monorepo.Cli.Output
{
    public class ConsoleOutputThemedText : IConsoleOutput
    {
        private readonly string text;
        private readonly Func<IConsoleTheme, ConsoleColor> colorProvider;

        public ConsoleOutputThemedText(string text, Func<IConsoleTheme, ConsoleColor> colorProvider)
        {
            this.text = text;
            this.colorProvider = colorProvider;
        }

        public void WriteTo(IExtendedConsole console)
        {
            var tConsole = console.Console;

            var foregroundColor = tConsole.ForegroundColor;
            tConsole.ForegroundColor = colorProvider(console.Theme);
            tConsole.Write(text);
            tConsole.ForegroundColor = foregroundColor;
        }
    }
}
