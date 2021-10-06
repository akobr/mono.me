using System.Collections.Generic;

namespace _42.Monorepo.Cli.Model.Records
{
    public interface ICompositionOfProjectRecords
    {
        IEnumerable<IProjectRecord> GetAllProjects();
    }
}
