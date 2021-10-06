using System;
using McMaster.Extensions.CommandLineUtils;

namespace _42.Monorepo.Cli.Output
{
    public class ConsoleOutputText : IConsoleOutput
    {
        private readonly string text;
        private readonly ConsoleColor? color;

        public ConsoleOutputText(string text)
        {
            this.text = text;
        }

        public ConsoleOutputText(string text, ConsoleColor color)
        {
            this.text = text;
            this.color = color;
        }

        public static implicit operator ConsoleOutputText(string text)
        {
            return new ConsoleOutputText(text);
        }

        public void WriteTo(IExtendedConsole console)
        {
            var tConsole = console.Console;

            if (!color.HasValue)
            {
                tConsole.Write(text);
                return;
            }

            var foregroundColor = tConsole.ForegroundColor;
            tConsole.ForegroundColor = color.Value;
            tConsole.Write(text);
            tConsole.ForegroundColor = foregroundColor;
        }
    }
}
