using System;

namespace _42.Monorepo.Cli.Model.Records
{
    public class InvalidItemRecord : ItemRecord
    {
        public InvalidItemRecord(string path)
            : base(path, null)
        {
            // no operation
        }

        public override ItemType Type
            => throw new InvalidOperationException("Invalid item has been used.");
    }
}
