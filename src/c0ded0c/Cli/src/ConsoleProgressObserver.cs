using System;
using c0ded0c.Core;

namespace c0ded0c.Cli
{
    internal class ConsoleProgressObserver : IProgress<IToolProgress>
    {
        public void Report(IToolProgress value)
        {
            Console.WriteLine(value.Message);
        }
    }
}
