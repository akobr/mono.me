using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Threading.Tasks;
using _42.CLI.Toolkit.Output;
using _42.Platform.Cli.Output;
using _42.Platform.Cli.Output.Exceptions;
using _42.Platform.Storyteller.Sdk;
using McMaster.Extensions.CommandLineUtils;

namespace _42.Platform.Cli.Commands.KeyValues;

// Every key/value command: the API client, and the API errors turned into messages and exit codes.
public abstract class KeyValueCommandBase : BaseContextCommand
{
    protected KeyValueCommandBase(IExtendedConsole console, ICommandContext context, IKeyValuesApiClient keyValues)
        : base(console, context)
    {
        KeyValues = keyValues;
    }

    protected IKeyValuesApiClient KeyValues { get; }

    protected sealed override async Task<int> ExecuteAsync()
    {
        try
        {
            return await ExecuteKeyValuesAsync();
        }
        catch (ApiException exception) when (exception.StatusCode != (int)HttpStatusCode.Unauthorized)
        {
            return Console.WriteApiError(exception);
        }
    }

    protected abstract Task<int> ExecuteKeyValuesAsync();

    protected string RequireKey(string? key)
    {
        if (string.IsNullOrWhiteSpace(key))
        {
            Console.WriteImportant("Please specify the key.");
            throw new WrongInputException();
        }

        return key.Trim();
    }
}

// Keys given as arguments, or every key under a prefix.
public abstract class KeyValueBulkCommandBase : KeyValueCommandBase
{
    protected KeyValueBulkCommandBase(IExtendedConsole console, ICommandContext context, IKeyValuesApiClient keyValues)
        : base(console, context, keyValues)
    {
    }

    [Argument(0, Description = "The keys to work on.")]
    public string[]? Keys { get; set; }

    [Option("--prefix", CommandOptionType.SingleValue, Description = "Every key under this prefix instead of a list of keys (at most 1000).")]
    public string? Prefix { get; set; }

    protected bool HasKeys => Keys is { Length: > 0 };

    protected bool HasPrefix => Prefix is not null;

    protected KeyValueBulkRequest BuildRequest()
    {
        if (HasKeys == HasPrefix)
        {
            Console.WriteImportant(HasKeys ? "Give either keys or --prefix, not both." : "Give the keys, or --prefix for every key under a prefix (an empty prefix means every key).");
            throw new WrongInputException();
        }

        return new KeyValueBulkRequest
        {
            Keys = HasKeys ? Keys!.Select(key => key.Trim()).ToList() : null,
            KeyPrefix = HasPrefix ? Prefix!.Trim() : null,
            Protection = null,
        };
    }

    protected string DescribeTarget()
    {
        return HasKeys
            ? Keys!.Length == 1 ? $"the key '{Keys[0]}'" : $"{Keys.Length} keys"
            : string.IsNullOrEmpty(Prefix) ? $"every key of the view '{Context.ViewName}'" : $"every key under '{Prefix}' in the view '{Context.ViewName}'";
    }
}
