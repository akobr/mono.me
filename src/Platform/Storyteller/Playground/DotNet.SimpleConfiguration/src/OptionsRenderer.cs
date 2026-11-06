using _42.Platform.Storyteller.Sdk.Configuring;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using Newtonsoft.Json;

namespace _42.Platform.Storyteller.Playground.DotNet.SimpleConfiguration;

// Writes the document and the options at start, then every change that reaches the monitor. The options name only a part
// of a document, so the lines come from the keys of the document: any key added on the platform shows.
//
// Prints the values of the document, so run it with a machine without Secrets.Read.
public sealed class OptionsRenderer : IHostedService
{
    private readonly Lock _lock = new();
    private readonly IOptionsMonitor<PlaygroundOptions> _monitor;
    private readonly IConfiguration _configuration;
    private readonly IStorytellerConfigurationRefresher _refresher;
    private readonly TextWriter _output;

    private IReadOnlyDictionary<string, string> _keys = new Dictionary<string, string>();
    private IDisposable? _subscription;

    public OptionsRenderer(
        IOptionsMonitor<PlaygroundOptions> monitor,
        IConfiguration configuration,
        IStorytellerConfigurationRefresher refresher,
        TextWriter output)
    {
        _monitor = monitor;
        _configuration = configuration;
        _refresher = refresher;
        _output = output;
    }

    public Task StartAsync(CancellationToken cancellationToken)
    {
        lock (_lock)
        {
            _subscription = _monitor.OnChange(Render);
            _keys = DocumentKeys.Read(_configuration);

            _output.WriteLine(_refresher.ActiveKey);

            foreach (var (key, value) in _keys)
            {
                _output.WriteLine($"  {key} = {Quote(value)}");
            }

            WriteOptions(_monitor.CurrentValue);
        }

        return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken cancellationToken)
    {
        _subscription?.Dispose();
        return Task.CompletedTask;
    }

    // Runs on the thread of the provider's notification. It must not throw: the provider counts a change callback that
    // throws as a document that does not bind, and rolls the document back.
    private void Render(PlaygroundOptions options)
    {
        lock (_lock)
        {
            var keys = DocumentKeys.Read(_configuration);
            var changes = ConfigurationDiff.Compare(_keys, keys);
            _keys = keys;

            _output.WriteLine($"{DateTimeOffset.Now:HH:mm:ss} IOptionsMonitor changed{(changes.Count == 0 ? ", no key did" : string.Empty)}");

            foreach (var change in changes)
            {
                _output.WriteLine(change.Kind switch
                {
                    KeyChangeKind.Added => $"  + {change.Key} = {Quote(change.After)}",
                    KeyChangeKind.Changed => $"  ~ {change.Key} = {Quote(change.Before)} -> {Quote(change.After)}",
                    _ => $"  - {change.Key} = {Quote(change.Before)}",
                });
            }

            WriteOptions(options);
        }
    }

    private void WriteOptions(PlaygroundOptions options)
    {
        _output.WriteLine($"Options: {JsonConvert.SerializeObject(options)}");
    }

    // A line end or a control character in a value does not break the line.
    private static string Quote(string? value)
    {
        return JsonConvert.ToString(value);
    }
}
