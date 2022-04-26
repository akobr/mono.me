using System;
using System.Collections.Generic;
using System.Collections.Immutable;

namespace c0ded0c.Core
{
    public partial class WorkspaceInfo : SubjectInfo, IWorkspaceInfo
    {
        private ImmutableDictionary<string, IAssemblyInfo> assemblies;

        public WorkspaceInfo(IIdentificator key)
            : this(
                  key,
                  ImmutableDictionary<string, IAssemblyInfo>.Empty.WithComparers(StringComparer.OrdinalIgnoreCase))
        {
            // no operation
        }

        public WorkspaceInfo(
            IIdentificator key,
            ImmutableDictionary<string, IAssemblyInfo> assemblies)
            : base(key)
        {
            this.assemblies = assemblies;
        }

        public WorkspaceInfo(IWorkspaceInfo toClone)
            : base(toClone)
        {
            assemblies = ImmutableDictionary.CreateRange(StringComparer.OrdinalIgnoreCase, toClone.Assemblies);
        }

        private WorkspaceInfo(Builder builder)
            : base(builder)
        {
            assemblies = builder.Assemblies.ToImmutable();
        }

        [Artifact]
        public IImmutableDictionary<string, IAssemblyInfo> Assemblies => assemblies;

        public override IEnumerable<ISubjectInfo> GetChildren()
        {
            return assemblies.Values;
        }

        public WorkspaceInfo SetAssembly(IAssemblyInfo assembly)
        {
            return new WorkspaceInfo(this)
            {
                assemblies = assemblies.Remove(assembly.Key.FullName).SetItem(assembly.Key.FullName, assembly),
            };
        }

        public Builder ToBuilder()
        {
            return new Builder(this);
        }

        public static WorkspaceInfo CreateEmpty(string name)
        {
            return new WorkspaceInfo(new Identificator(Guid.NewGuid().ToString(), name, name, "."));
        }
    }
}
