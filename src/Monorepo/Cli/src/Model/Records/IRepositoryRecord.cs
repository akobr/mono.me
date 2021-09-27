using System.Collections.Generic;

namespace _42.Monorepo.Cli.Model.Records
{
    public interface IRepositoryRecord : IItemRecord
    {
        public bool IsValid { get; }

        IReadOnlyCollection<IWorksteadRecord> GetWorksteads();
    }
}
