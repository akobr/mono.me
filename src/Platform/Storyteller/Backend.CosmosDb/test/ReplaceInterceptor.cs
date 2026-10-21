using System;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Azure.Cosmos;

namespace _42.Platform.Storyteller.Backend.CosmosDb.UnitTests;

// Runs an action before each replace of one document, so a test can put a concurrent write between a read and its
// replace. The action gets the number of the replace, from 1.
public sealed class ReplaceInterceptor(string id, Func<int, Task> beforeReplace) : RequestHandler
{
    public int Replaces { get; private set; }

    public override async Task<ResponseMessage> SendAsync(RequestMessage request, CancellationToken cancellationToken)
    {
        if (request.Method == HttpMethod.Put
            && request.RequestUri.OriginalString.EndsWith($"/docs/{id}", StringComparison.Ordinal))
        {
            await beforeReplace(++Replaces);
        }

        return await base.SendAsync(request, cancellationToken);
    }

    // A client like the one of CosmosClientProvider, with this handler in its pipeline.
    public CosmosClient CreateClient(CosmosDbOptions options)
    {
        var clientOptions = new CosmosClientOptions
        {
            SerializerOptions = new()
            {
                PropertyNamingPolicy = CosmosPropertyNamingPolicy.Default,
                IgnoreNullValues = true,
            },
        };

        if (options.ShouldAcceptAnyCertificate ?? false)
        {
            clientOptions.HttpClientFactory = () => new HttpClient(new HttpClientHandler
            {
                ServerCertificateCustomValidationCallback = HttpClientHandler.DangerousAcceptAnyServerCertificateValidator,
            });
            clientOptions.ConnectionMode = ConnectionMode.Gateway;
            clientOptions.LimitToEndpoint = true;
        }

        clientOptions.CustomHandlers.Add(this);
        return new CosmosClient(options.Connection, clientOptions);
    }
}
