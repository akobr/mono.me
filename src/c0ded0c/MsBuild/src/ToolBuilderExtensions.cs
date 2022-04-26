using System;
using c0ded0c.Core;
using c0ded0c.MsBuild.Genesis;
using Microsoft.Extensions.DependencyInjection;

namespace c0ded0c.MsBuild
{
    public static class ToolBuilderExtensions
    {
        public static IToolBuilder UseCSharp(this IToolBuilder builder)
        {
            UseCSharpMining(builder);
            UseMsBuildProjectProcessing(builder);
            UseRoslynAssemblyProcessing(builder);
            UseSymbolUsagesGenesis(builder);
            return builder;
        }

        public static IToolBuilder UseCSharpMining(this IToolBuilder builder)
        {
            return builder.ConfigureMiningEngine(
                miningBuilder => miningBuilder.Use<MsBuildMiningMiddleware>());
        }

        public static IToolBuilder UseMsBuildProjectProcessing(this IToolBuilder builder)
        {
            return builder.ConfigureProjectProcessingEngine(
                projectProcessingBuilder => projectProcessingBuilder.Use<MsBuildProjectProcessingMiddleware>());
        }

        public static IToolBuilder UseRoslynAssemblyProcessing(this IToolBuilder builder)
        {
            builder.ConfigureServices((services) => services.AddSingleton<INamespaceManager, NamespaceManager>());
            return builder.ConfigureAssemblyProcessingEngine(
                projectProcessingBuilder => projectProcessingBuilder.Use<RoslynAssemblyProcessingMiddleware>());
        }

        public static IToolBuilder UseSymbolUsagesGenesis(IToolBuilder builder)
        {
            return builder.ConfigureGenesisEngine(
                genesisBuilder => genesisBuilder.Use<SymbolUsagesGenesisMiddleware>());
        }
    }
}
