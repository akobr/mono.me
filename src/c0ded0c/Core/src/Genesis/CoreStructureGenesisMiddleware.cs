using System;
using System.Collections;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;

namespace c0ded0c.Core.Genesis
{
    public class CoreStructureGenesisMiddleware : IGenesisMiddleware
    {
        private readonly IArtifactManager artifactManager;

        public CoreStructureGenesisMiddleware(IArtifactManager artifactManager)
        {
            this.artifactManager = artifactManager ?? throw new ArgumentNullException(nameof(artifactManager));
        }

        public async Task<IWorkspaceInfo> ShapeAsync(IWorkspaceInfo workspace, GenesisAsyncDelegate next)
        {
            List<Task> tasks = new List<Task>();

            tasks.Add(StoreCoreArtifacts(workspace));

            foreach (IAssemblyInfo assembly in workspace.Assemblies.Values)
            {
                tasks.Add(StoreCoreArtifacts(assembly));

                foreach (INamespaceInfo @namespace in assembly.Namespaces.Values)
                {
                    tasks.Add(StoreCoreArtifacts(@namespace));
                }

                foreach (IDocumentInfo document in assembly.Documents.Values)
                {
                    tasks.Add(StoreCoreArtifacts(document));
                }

                foreach (ITypeInfo type in assembly.Types.Values)
                {
                    tasks.Add(StoreCoreArtifacts(type));
                }
            }

            await Task.WhenAll(tasks);
            return await next(workspace);
        }

        // TODO: [P1] refactor this and solve all nullable reference types
        private Task StoreCoreArtifacts(ISubjectInfo info)
        {
            List<Task> tasks = new List<Task>();
            Type type = info.GetType();

            Dictionary<string, object> infoProperties = new Dictionary<string, object>();
            foreach (PropertyInfo property in type.GetProperties().Where(p => p.GetCustomAttribute<InfoPropertyAttribute>() != null))
            {
                infoProperties.Add(property.Name, property.GetGetMethod().Invoke(info, null));
            }

            tasks.Add(artifactManager.CreateAsync(new SubjectInfoModel(info, infoProperties), Constants.INFO_KEY, info));

            foreach (PropertyInfo property in type.GetProperties().Where(p => p.GetCustomAttribute<ArtifactAttribute>() != null))
            {
                Type propertyType = property.PropertyType;

                if (!propertyType.IsInterface || !propertyType.IsGenericType)
                {
                    continue;
                }

                Type genericType = propertyType.GetGenericTypeDefinition();
                object? artifactValue = null;

                if (genericType == typeof(IImmutableSet<>)
                    && propertyType.GenericTypeArguments[0] == typeof(IIdentificator))
                {
                    IImmutableSet<IIdentificator> set = (IImmutableSet<IIdentificator>)property.GetGetMethod().Invoke(info, null);

                    if (set == null || set.Count < 1)
                    {
                        continue;
                    }

                    artifactValue = set;
                }
                else if (genericType == typeof(IImmutableDictionary<,>)
                         && propertyType.GenericTypeArguments[0] == typeof(string)
                         && typeof(ISubjectInfo).IsAssignableFrom(propertyType.GenericTypeArguments[1]))
                {
                    var directoryValue = property.GetGetMethod().Invoke(info, null);
                    Type typeValue = directoryValue.GetType();

                    if (directoryValue == null
                        || (int)typeValue.GetProperty(nameof(IDictionary.Count)).GetGetMethod().Invoke(directoryValue, null) < 1)
                    {
                        continue;
                    }

                    PropertyInfo propertyValue = typeValue.GetProperty(nameof(IDictionary.Values));
                    var values = propertyValue.GetGetMethod().Invoke(directoryValue, null);
                    artifactValue = Enumerable.Cast<ISubjectInfo>((IEnumerable)values).Select((s) => new SubjectStoreModel(s));
                }
                else
                {
                    continue;
                }

                tasks.Add(artifactManager.CreateAsync(artifactValue, property.Name, info));
            }

            return Task.WhenAll(tasks);
        }
    }
}
