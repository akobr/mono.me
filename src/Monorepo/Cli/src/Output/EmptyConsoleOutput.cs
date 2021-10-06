namespace _42.Monorepo.Cli.Output
{
    public class EmptyConsoleOutput : IConsoleOutput
    {
        public void WriteTo(IExtendedConsole console)
        {
            // no operation
        }
    }
}
