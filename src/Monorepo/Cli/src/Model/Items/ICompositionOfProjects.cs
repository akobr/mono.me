using System.Collections.Generic;

namespace _42.Monorepo.Cli.Model.Items
{
    public interface ICompositionOfProjects
    {
        IEnumerable<IProject> GetAllProjects();
    }
}
