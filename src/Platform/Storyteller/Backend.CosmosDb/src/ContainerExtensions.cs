using System.Net;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Azure.Cosmos;

using Container = Microsoft.Azure.Cosmos.Container;
using PartitionKey = Microsoft.Azure.Cosmos.PartitionKey;

namespace _42.Platform.Storyteller;

public static class ContainerExtensions
{
    // TODO: [P2] use default serializer same like Azure Function runtime is using
    private static readonly JsonSerializerOptions Options = new(JsonSerializerOptions.Default)
    {
        PropertyNamingPolicy = new NoChangeNamingPolicy(),
        Converters = { new JsonStringEnumConverter() },
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        PropertyNameCaseInsensitive = true,
        AllowTrailingCommas = true,
    };

    public static async Task<TItem?> TryReadItem<TItem>(
        this Container @this,
        string id,
        PartitionKey partitionKey,
        ItemRequestOptions? requestOptions = null,
        CancellationToken cancellationToken = default)
        where TItem : class
    {
        using var response = await @this.ReadItemStreamAsync(id, partitionKey, requestOptions, cancellationToken);

        if (response.StatusCode == HttpStatusCode.NotFound)
        {
            return default;
        }

        var item = JsonSerializer.Deserialize<TItem>(response.Content, Options);
        return item;
    }
}
