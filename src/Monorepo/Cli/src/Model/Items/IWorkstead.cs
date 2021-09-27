using System.Collections.Generic;
using _42.Monorepo.Cli.Model.Records;

namespace _42.Monorepo.Cli.Model.Items
{
    public interface IWorkstead : IItem
    {
        new IWorksteadRecord Record { get; }

        IReadOnlyCollection<IWorkstead> GetSubWorksteads();

        IReadOnlyCollection<IProject> GetSubProjects();
    }
}
