using System.Runtime.InteropServices;
using _42.Monorepo.Repo.Generator.Strategies;
using QuikGraph;

namespace _42.Monorepo.Repo.Generator
{
    internal class WorkOptions
    {
        public IWorkStrategy Work { get; set; } = new EmptyStrategy();

        public string RepositoryPath { get; set; } = string.Empty;

        public string BranchName { get; set; } = "task/work";

        public string Workstead { get; set; } = string.Empty;

        public int Complexity { get; set; } = 5;

        public IBidirectionalGraph<string, IEdge<string>> DependencyGraph { get; set; } = new BidirectionalGraph<string, IEdge<string>>();

        public string WorkType
        {
            get
            {
                return Work switch
                {
                    EmptyStrategy => "Empty",
                    FixStrategy => "Fix",
                    FeatureStrategy => "Feature",
                    WorksteadStrategy => "NewProject",
                    _ => string.Empty,
                };
            }
        }
    }
}
