using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using c0ded0c.Core;
using c0ded0c.Core.Genesis;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.FindSymbols;

namespace c0ded0c.MsBuild.Genesis
{
    public class SymbolUsagesGenesisMiddleware : IGenesisMiddleware
    {
        private readonly IIdentificationMap identificationMap;
        private readonly IArtifactManager artifactManager;

        public SymbolUsagesGenesisMiddleware(
            IIdentificationMap identificationMap,
            IArtifactManager artifactManager)
        {
            this.identificationMap = identificationMap ?? throw new ArgumentNullException(nameof(identificationMap));
            this.artifactManager = artifactManager ?? throw new ArgumentNullException(nameof(artifactManager));
        }

        public async Task<IWorkspaceInfo> ShapeAsync(IWorkspaceInfo workspace, GenesisAsyncDelegate next)
        {
            List<Task> tasks = new List<Task>();

            foreach (IAssemblyInfo assembly in workspace.Assemblies.Values)
            {
                Project? project = assembly.GetMutableTag<Project>();

                if (project == null)
                {
                    continue;
                }

                foreach (ITypeInfo type in assembly.Types.Values)
                {
                    tasks.Add(BuildTypeAndMembersUsagesAsync(type, assembly, project));
                }
            }

            await Task.WhenAll(tasks);
            return await next(workspace);
        }

        private async Task BuildTypeAndMembersUsagesAsync(ITypeInfo type, IAssemblyInfo assembly, Project project)
        {
            INamedTypeSymbol? typeSymbol = type.GetMutableTag<INamedTypeSymbol>();

            if (typeSymbol == null)
            {
                return;
            }

            await BuildTypeUsagesAsync(typeSymbol, type, assembly, project);
            await BuildTypeMembersUsagesAsync(type, assembly, project);
        }

        private async Task BuildTypeUsagesAsync(INamedTypeSymbol typeSymbol, ITypeInfo type, IAssemblyInfo assembly, Project project)
        {
            List<PositionInfo> positions = await BuildSymbolPositionsAsync(typeSymbol, assembly, project.Solution);

            if (positions.Count < 1)
            {
                return;
            }

            await artifactManager.CreateAsync(positions, ArtifactKeys.USAGES, type);
        }

        private async Task BuildTypeMembersUsagesAsync(ITypeInfo type, IAssemblyInfo assembly, Project project)
        {
            Dictionary<string, IEnumerable<PositionInfo>> positionMap = new Dictionary<string, IEnumerable<PositionInfo>>();
            foreach (IMemberInfo member in type.Members.Values)
            {
                ISymbol? memberSymbol = member.GetMutableTag<ISymbol>();

                if (memberSymbol == null)
                {
                    continue;
                }

                List<PositionInfo> positions = await BuildSymbolPositionsAsync(memberSymbol, assembly, project.Solution);

                if (positions.Count < 1)
                {
                    continue;
                }

                positionMap.Add(member.Name, positions);
            }

            if (positionMap.Count < 1)
            {
                return;
            }

            await artifactManager.CreateAsync(positionMap, ArtifactKeys.MEMBER_USAGES, type);
        }

        private async Task<List<PositionInfo>> BuildSymbolPositionsAsync(ISymbol symbol, IAssemblyInfo assembly, Solution solution)
        {
            var references = await SymbolFinder.FindReferencesAsync(symbol, solution);
            List<PositionInfo> positions = new List<PositionInfo>();

            foreach (ReferenceLocation location in references.SelectMany(r => r.Locations))
            {
                IIdentificator? documentKey = identificationMap.GetByFullName(location.Document.BuildFullName(assembly.Key));

                if (documentKey == null)
                {
                    continue;
                }

                positions.Add(new PositionInfo(documentKey, location.Location.SourceSpan));
            }

            return positions;
        }
    }
}
