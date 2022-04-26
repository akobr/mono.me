using c0ded0c.Core;
using c0ded0c.PlantUml.Genesis;

namespace c0ded0c.PlantUml
{
    public static class ToolBuilderExtensions
    {
        public static IToolBuilder UseClassDiagrams(this IToolBuilder builder)
        {
            return builder.ConfigureGenesisEngine(
                genesisBuilder => genesisBuilder.Use<PlantUmlGenesisMiddleware>());
        }
    }
}
