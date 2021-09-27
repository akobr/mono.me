using System.Collections.Generic;

namespace _42.Monorepo.Cli.Model.Records
{
    public interface IWorksteadRecord : IItemRecord
    {
        bool IsTopLevel { get; }

        IReadOnlyCollection<IWorksteadRecord> GetSubWorksteads();

        IReadOnlyCollection<IProjectRecord> GetProjects();
    }
}
