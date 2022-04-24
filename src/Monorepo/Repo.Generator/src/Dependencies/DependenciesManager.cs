using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using QuikGraph;
using QuikGraph.Serialization;

namespace _42.Monorepo.Repo.Generator.Dependencies
{
    internal class DependenciesManager
    {
        private readonly Random _random = new();
        private readonly BidirectionalGraph<string, IEdge<string>> _dependencyGraph = new(false);

        private string _graphFilePath = string.Empty;
        private IReadOnlyList<string> _componentProjects = Array.Empty<string>();

        public DependenciesManager()
        {
            _dependencyGraph.AddVertex("root");
        }

        public DependenciesManager(
            BidirectionalGraph<string, IEdge<string>> initialGraph,
            string graphFilePath,
            IReadOnlyList<string> componentProjects)
        {
            _dependencyGraph = initialGraph;
            _graphFilePath = graphFilePath;
            _componentProjects = componentProjects;
        }

        public IBidirectionalGraph<string, IEdge<string>> Graph => _dependencyGraph;

        public string GraphFilePath => _graphFilePath;

        public void Initialise(string initPath)
        {
            _graphFilePath = Path.Combine(initPath, "dependency-graph.xml");
            LoadDependencyGraph();

            var repoStatsFilePath = Path.Combine(initPath, "components.list");
            _componentProjects = File.ReadAllLines(repoStatsFilePath);
        }

        public void AddWorksteadToDependencyGraph(IReadOnlyList<string> projects)
        {
            var mainProjectIndex = _random.Next(0, projects.Count);
            var mainProject = projects[mainProjectIndex];
            _dependencyGraph.AddVertex(mainProject);
            _dependencyGraph.AddEdge(new Edge<string>("root", mainProject));

            for (var i = 0; i < projects.Count; i++)
            {
                if (i == mainProjectIndex)
                {
                    continue;
                }

                var project = projects[i];
                _dependencyGraph.AddVertex(project);
                _dependencyGraph.AddEdge(new Edge<string>(mainProject, project));

                var componentProject = _componentProjects[_random.Next(0, _componentProjects.Count)];
                _dependencyGraph.AddEdge(new Edge<string>(project, componentProject));

                if (_random.NextDouble() < 0.34)
                {
                    var secondComponentProject = string.Empty;

                    do
                    {
                        secondComponentProject = _componentProjects[_random.Next(0, _componentProjects.Count)];
                    }
                    while (secondComponentProject == componentProject);

                    _dependencyGraph.AddEdge(new Edge<string>(project, secondComponentProject));
                }
            }
        }

        public void SaveDependencyGraph()
        {
            _dependencyGraph
                .SerializeToGraphML<string, IEdge<string>, BidirectionalGraph<string, IEdge<string>>>(_graphFilePath);
        }

        private void LoadDependencyGraph()
        {
            _dependencyGraph.DeserializeFromGraphML(
                _graphFilePath,
                id => id,
                (source, target, id) => (IEdge<string>)new Edge<string>(source, target));
        }


    }
}
