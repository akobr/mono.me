using _42.CLI.Toolkit.Output;
using _42.Platform.Storyteller.Sdk;

namespace _42.Platform.Cli.Commands.Schemas;

public abstract class SchemaApiCommand : BaseContextCommand
{
    protected SchemaApiCommand(
        IExtendedConsole console,
        ICommandContext context,
        ISchemasApiClient schemasApi)
        : base(console, context)
    {
        SchemasApi = schemasApi;
    }

    protected ISchemasApiClient SchemasApi { get; }
}
