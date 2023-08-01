using System.Linq;

namespace _42.CLI.Toolkit.Output
{
    public class ConsoleOutput : IConsoleOutput
    {
        private readonly IConsoleOutput wrappedOutput;

        public ConsoleOutput(IConsoleOutput text)
        {
            wrappedOutput = text;
        }

        public ConsoleOutput(object? item)
        {
            switch (item)
            {
                case null:
                    wrappedOutput = new EmptyConsoleOutput();
                    break;

                case IConsoleOutput writable:
                    wrappedOutput = writable;
                    break;

                default:
                    var text = item.ToString();
                    wrappedOutput = text is not null
                        ? new ConsoleOutputText(text)
                        : new EmptyConsoleOutput();
                    break;
            }
        }

        public static implicit operator ConsoleOutput(string text)
        {
            return new ConsoleOutput(new ConsoleOutputText(text));
        }

        public static implicit operator ConsoleOutput(IConsoleOutput[] items)
        {
            return new ConsoleOutput(new ConsoleOutputComposition(items));
        }

        public static implicit operator ConsoleOutput(object[] items)
        {
            return new ConsoleOutput(new ConsoleOutputComposition(items.Select(i => new ConsoleOutput(i))));
        }

        public void WriteTo(IExtendedConsole console)
        {
            wrappedOutput.WriteTo(console);
        }
    }
}
