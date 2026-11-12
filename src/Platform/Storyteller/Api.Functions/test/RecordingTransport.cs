using System.Collections.Concurrent;
using System.Text;
using System.Text.Json;

using Sentry.Extensibility;
using Sentry.Protocol.Envelopes;

namespace _42.Platform.Storyteller.Api.Functions.UnitTests;

// Instead of the network: keeps every envelope as it would be sent, the wire form the assertions read.
internal sealed class RecordingTransport : ITransport
{
    public ConcurrentQueue<string> Envelopes { get; } = new();

    // The payloads of the items of type "event", parsed.
    public IReadOnlyList<JsonElement> Events => Envelopes.SelectMany(ReadEvents).ToList();

    public string AllText => string.Join('\n', Envelopes);

    public async Task SendEnvelopeAsync(Envelope envelope, CancellationToken cancellationToken = default)
    {
        using var stream = new MemoryStream();
        await envelope.SerializeAsync(stream, null, cancellationToken);
        Envelopes.Enqueue(Encoding.UTF8.GetString(stream.ToArray()));
    }

    // An envelope: its header line, then a header line and a payload line per item.
    private static IEnumerable<JsonElement> ReadEvents(string envelope)
    {
        var lines = envelope.Split('\n');

        for (var index = 1; index + 1 < lines.Length; index += 2)
        {
            using var header = JsonDocument.Parse(lines[index]);

            if (header.RootElement.TryGetProperty("type", out var type) && type.GetString() == "event")
            {
                yield return JsonDocument.Parse(lines[index + 1]).RootElement.Clone();
            }
        }
    }
}
