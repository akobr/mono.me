using System.Threading.Tasks;
using McMaster.Extensions.CommandLineUtils;

namespace c0ded0c.Cli
{
    internal class Program
    {
        public static Task Main(string[] args)
        {
            return CommandLineApplication.ExecuteAsync<C0ded0cCommand>(args);
        }
    }
}
