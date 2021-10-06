using System.Collections.Generic;

namespace _42.Monorepo.Cli.Model.Records
{
    public interface IWorksteadRecord : IRecord, ICompositionOfProjectRecords
    {
        bool IsTopLevel { get; }

        IReadOnlyCollection<IWorksteadRecord> GetSubWorksteads();

        IReadOnlyCollection<IProjectRecord> GetProjects();
    }
}
