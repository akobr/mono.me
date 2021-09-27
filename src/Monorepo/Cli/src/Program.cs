using System;
using System.Drawing;
using System.Threading.Tasks;
using _42.Monorepo.Cli.Commands;
using McMaster.Extensions.CommandLineUtils;

using Console = Colorful.Console;

namespace _42.Monorepo.Cli
{
    public class Program
    {
        public static async Task<int> Main(string[] args)
        {
            try
            {
                return await CommandLineApplication.ExecuteAsync<MonorepoCommand>(args);
            }
            catch (Exception e)
            {
                Console.WriteWithGradient("Total mayhem", Color.Yellow, Color.Magenta);
                Console.WriteLine(", the tool is broken! ):", Color.Magenta);
                Console.WriteLine($"Error: {e.Message}", Color.DarkRed);
                Console.WriteLine($"For more info check the log file at {"XX/X"}", Color.DarkGray);

                // TODO: logging
                throw;
            }
        }
    }
}
