using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using c0ded0c.Core;
using c0ded0c.Core.Genesis;
using c0ded0c.PlantUml.Library;
using Microsoft.CodeAnalysis;

using TypeInfo = c0ded0c.Core.TypeInfo;

namespace c0ded0c.PlantUml.Genesis
{
    public class PlantUmlGenesisMiddleware : IGenesisMiddleware
    {
        private readonly IArtifactManager artifactManager;

        public PlantUmlGenesisMiddleware(IArtifactManager artifactManager)
        {
            this.artifactManager = artifactManager ?? throw new ArgumentNullException(nameof(artifactManager));
        }

        public async Task<IWorkspaceInfo> ShapeAsync(IWorkspaceInfo workspace, GenesisAsyncDelegate next)
        {
            List<Task> tasks = new List<Task>();

            foreach (IAssemblyInfo assembly in workspace.Assemblies.Values)
            {
                foreach (ITypeInfo type in assembly.Types.Values)
                {
                    tasks.Add(StorePlantUmlArtifactAsync(type));
                }

                foreach (INamespaceInfo @namespace in assembly.Namespaces.Values)
                {
                    tasks.Add(StorePlantUmlArtifactAsync(@namespace, assembly));
                }

                foreach (IDocumentInfo document in assembly.Documents.Values)
                {
                    tasks.Add(StorePlantUmlArtifactAsync(document, assembly));
                }
            }

            await Task.WhenAll(tasks);
            return await next(workspace);
        }

        private Task StorePlantUmlArtifactAsync(ITypeInfo type)
        {
            TypeInfo familiarType = TypeInfo.From(type);
            var typeSymbol = familiarType.GetMutableTag<INamedTypeSymbol>();

            if (typeSymbol == null)
            {
                return Task.CompletedTask;
            }

            return Task.Run(() => StorePlantUmlArtifactAsync(typeSymbol.DeclaringSyntaxReferences, type));
        }

        private Task StorePlantUmlArtifactAsync(INamespaceInfo @namespace, IAssemblyInfo assembly)
        {
            return Task.Run(() => StorePlantUmlArtifactAsync(
                GetSyntaxReferences(@namespace.Types.Select(ti => assembly.Types[ti.FullName])),
                @namespace));
        }

        private Task StorePlantUmlArtifactAsync(IDocumentInfo document, IAssemblyInfo assembly)
        {
            return Task.Run(() => StorePlantUmlArtifactAsync(
                GetSyntaxReferences(document.Types.Select(ti => assembly.Types[ti.FullName])),
                document));
        }

        private async Task StorePlantUmlArtifactAsync(IEnumerable<SyntaxReference> references, ISubjectInfo subject)
        {
            using (StringWriter writer = new StringWriter())
            {
                var generator = new ClassDiagramGenerator(writer, string.Empty, Accessibilities.None, false);
                generator.StartBatchGeneration();

                foreach (SyntaxReference reference in references)
                {
                    SyntaxNode syntax = await reference.GetSyntaxAsync();
                    generator.GenerateInBatch(syntax);
                }

                generator.FinishBatchGeneration();
                await artifactManager.CreateSpecificAsync(
                    new StringContent(writer.ToString(), ContentType.PlantUml),
                    ArtifactKeys.CLASS_DIAGRAM,
                    subject);
            }
        }

        private static IEnumerable<SyntaxReference> GetSyntaxReferences(IEnumerable<ITypeInfo> types)
        {
            foreach (ITypeInfo type in types)
            {
                TypeInfo familiarType = TypeInfo.From(type);
                var typeSymbol = familiarType.GetMutableTag<INamedTypeSymbol>();

                if (typeSymbol != null)
                {
                    foreach (SyntaxReference reference in typeSymbol.DeclaringSyntaxReferences)
                    {
                        yield return reference;
                    }
                }
            }
        }
    }
}
