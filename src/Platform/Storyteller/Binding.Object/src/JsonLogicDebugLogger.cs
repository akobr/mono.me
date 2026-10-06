using System.Text.Json.Nodes;
using Json.Logic;
using Microsoft.Extensions.Logging;

namespace _42.Platform.Storyteller.Binding.Object;

/// <summary>
/// Replaces JSON Logic's console log sink. Messages go to <see cref="ILogger"/> at Debug when a host
/// logger is supplied, and are dropped otherwise.
/// </summary>
internal sealed class JsonLogicDebugLogger : ILogicLogger
{
    public static readonly JsonLogicDebugLogger Instance = new();

    private ILogger? _logger;

    private JsonLogicDebugLogger()
    {
    }

    public void Use(ILogger? logger)
    {
        _logger = logger;
    }

    public void WriteLine(JsonNode? node)
    {
        _logger?.LogDebug("JSON Logic log: {Value}", node?.ToJsonString());
    }
}
