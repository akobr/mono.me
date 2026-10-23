using System.Reflection;
using System.Text;

using _42.Platform.Storyteller.Binding;
using _42.Platform.Storyteller.Binding.Language;
using _42.Platform.Storyteller.Binding.Object;
using _42.Platform.Storyteller.Configuring;

namespace _42.Platform.Storyteller.Api.ConditionalReads;

// What a resolve runs on besides its inputs, an input of the d tags (ConfigurationEntityTags.ForDependencies): a manifest
// stands for the resolved body only under the code and the registry that recorded it.
//   code      the module version ids of the assemblies that take part in a resolve and the informational version of the
//             API, once per process. A deterministic build changes the MVID whenever the compiled code changes; the
//             informational version alone changes per commit and would miss a build of uncommitted changes.
//   registry  the sorted names of the registered sources and functions, per read: the registry stays writable.
public static class BindingRuntimeVersion
{
    private static readonly Lazy<string> Code = new(ReadCode);

    public static string Of(IBindingCatalog? catalog)
    {
        if (catalog is null)
        {
            return Code.Value;
        }

        var bindings = catalog.GetCatalog();
        var builder = new StringBuilder(Code.Value);
        builder.Append("|sources:").AppendJoin(',', bindings.Sources.Select(source => source.Name));
        builder.Append("|functions:").AppendJoin(',', bindings.Functions.Select(function => function.Name));
        return builder.ToString();
    }

    private static string ReadCode()
    {
        Type[] anchors =
        [
            typeof(BindingRuntimeVersion),
            typeof(BindingScope),
            typeof(BindingExecutor),
            typeof(global::Json.JsonE.JsonE),
            typeof(ConfigurationBindingResolver),
            typeof(KeyValueBindingSource),
            typeof(KeyVaultBindingSource),
            typeof(IConfigurationService),
            typeof(CosmosConfigurationService),
            typeof(global::Json.Logic.JsonLogic),
        ];

        var version = typeof(BindingRuntimeVersion).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion;
        return $"{version}|{string.Join(',', anchors.Select(anchor => anchor.Assembly.ManifestModule.ModuleVersionId.ToString("N")))}";
    }
}
