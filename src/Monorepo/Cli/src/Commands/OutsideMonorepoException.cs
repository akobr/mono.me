using System;

namespace _42.Monorepo.Cli.Commands
{
    public class OutsideMonorepoException : Exception
    {
        public OutsideMonorepoException(string message)
            : base(message)
        {
            // no operation
        }
    }
}
