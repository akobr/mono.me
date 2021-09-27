using System;

namespace _42.Monorepo.Cli.Model
{
    public interface IIdentifier : IEquatable<IIdentifier>
    {
        public string Normalized { get; }

        public string Humanized { get; }

        public string Hash { get; }
    }
}
