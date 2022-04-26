using System;
using System.Collections.Generic;
using System.Collections.Immutable;

namespace c0ded0c.Core
{
    public sealed partial class ProjectInfo : SubjectInfo, IProjectInfo
    {
        private ImmutableDictionary<string, IProjectInfo> projects;
        private IAssemblyInfo? assembly;

        public ProjectInfo(IIdentificator key, string path)
            : this(
                key,
                path,
                ImmutableDictionary<string, IProjectInfo>.Empty.WithComparers(StringComparer.OrdinalIgnoreCase))
        {
            // no operation
        }

        public ProjectInfo(
            IIdentificator key,
            string path,
            ImmutableDictionary<string, IProjectInfo> projects)
            : base(key)
        {
            Path = path;
            this.projects = projects;
        }

        public ProjectInfo(IProjectInfo toClone)
            : base(toClone)
        {
            Path = toClone.Path;
            projects = ImmutableDictionary.CreateRange(StringComparer.OrdinalIgnoreCase, toClone.Projects);
        }

        private ProjectInfo(ProjectInfo toClone)
            : base(toClone)
        {
            Path = toClone.Path;
            projects = toClone.projects;
            assembly = toClone.assembly;
        }

        private ProjectInfo(Builder builder)
            : base(builder)
        {
            Path = builder.Path;
            projects = builder.Projects.ToImmutable();
            assembly = builder.Assembly;
        }

        [InfoProperty]
        public string Path { get; private set; }

        public IAssemblyInfo? Assembly => assembly;

        [Artifact]
        public IImmutableDictionary<string, IProjectInfo> Projects => projects;

        [InfoProperty]
        public bool IsAggregated => projects.Count > 0;

        public override IEnumerable<ISubjectInfo> GetChildren()
        {
            return projects.Values;
        }

        public ProjectInfo SetPath(string path)
        {
            return new ProjectInfo(this)
            {
                Path = path,
            };
        }

        public ProjectInfo SetProject(IProjectInfo subProject)
        {
            return new ProjectInfo(this)
            {
                projects = projects.Remove(subProject.Key.FullName).SetItem(subProject.Key.FullName, subProject),
            };
        }

        public ProjectInfo RemoveProject(string relativePath)
        {
            return new ProjectInfo(this)
            {
                projects = projects.Remove(relativePath),
            };
        }

        public ProjectInfo SetAssembly(IAssemblyInfo assembly)
        {
            return new ProjectInfo(this)
            {
                assembly = assembly,
            };
        }

        public ProjectInfo SetExpansion(IExpansion expansion)
        {
            return SetExpansion(new ProjectInfo(this), expansion);
        }

        public Builder ToBuilder()
        {
            return new Builder(this);
        }

        public static ProjectInfo From(IProjectInfo project)
        {
            return project is ProjectInfo familiar
                ? familiar
                : new ProjectInfo(project);
        }
    }
}
