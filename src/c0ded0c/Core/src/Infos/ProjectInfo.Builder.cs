using System.Collections.Immutable;

namespace c0ded0c.Core
{
    public partial class ProjectInfo : SubjectInfo, IProjectInfo
    {
        public sealed class Builder : BaseBuilder
        {
            internal Builder(ProjectInfo project)
            : base(project)
            {
                Path = project.Path;
                Projects = project.projects.ToBuilder();
                Assembly = project.assembly;
            }

            public string Path { get; set; }

            public IAssemblyInfo? Assembly { get; set; }

            public ImmutableDictionary<string, IProjectInfo>.Builder Projects { get; private set; }

            public void AddProject(IProjectInfo project)
            {
                Projects.Add(project.Key.FullName, project);
            }

            public ProjectInfo ToImmutable()
            {
                return new ProjectInfo(this);
            }
        }
    }
}
