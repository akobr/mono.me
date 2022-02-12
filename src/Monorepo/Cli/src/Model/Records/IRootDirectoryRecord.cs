using System.Collections.Generic;

namespace _42.Monorepo.Cli.Model.Records
{
    public interface IRootDirectoryRecord : IRecord, ICompositionOfProjectRecords
    {
        IReadOnlyCollection<IWorksteadRecord> GetWorksteads();
    }
}
