using System;

namespace _42.Monorepo.Cli.Model.Records
{
    internal class InvalidRecord : Record
    {
        public InvalidRecord(string path)
            : base(path, null)
        {
            // no operation
        }

        public override RecordType Type
            => throw new InvalidOperationException("An invalid record has been used.");
    }
}
