using System.Collections.Generic;
using _42.Monorepo.Cli.Model.Records;

namespace _42.Monorepo.Cli.Model.Items
{
    public interface IRepository : IItem, ICompositionOfProjects
    {
        new IRepositoryRecord Record { get; }

        IReadOnlyCollection<IRootDirectory> GetDirectories();

        IReadOnlyCollection<IWorkstead> GetWorksteads();
    }
}
