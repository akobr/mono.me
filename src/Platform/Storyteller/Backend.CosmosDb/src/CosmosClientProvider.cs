using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using Microsoft.Azure.Cosmos;
using Microsoft.Extensions.Options;

namespace _42.Platform.Storyteller
{
    public class CosmosClientProvider : IDisposable, ICosmosClientProvider
    {
        private readonly Lazy<CosmosClient> _client;
        private readonly CosmosDbOptions _options;
        private readonly IReadOnlyList<RequestHandler> _handlers;
        private SocketsHttpHandler? _emulatorHandler;

        // The request handlers registered in the services join the client's pipeline (the Cosmos DB tests check every
        // organization-wide query with one).
        public CosmosClientProvider(
            IOptions<CosmosDbOptions> options,
            IEnumerable<RequestHandler>? handlers = null)
        {
            _client = new Lazy<CosmosClient>(BuildClient);
            _options = options.Value;
            _handlers = handlers?.ToList() ?? [];
        }

        public CosmosClient Client => _client.Value;

        public void Dispose()
        {
            if (_client.IsValueCreated)
            {
                _client.Value.Dispose();
            }

            _emulatorHandler?.Dispose();
        }

        private CosmosClient BuildClient()
        {
            CosmosClientOptions options = new()
            {
                SerializerOptions = new()
                {
                    PropertyNamingPolicy = CosmosPropertyNamingPolicy.Default,
                    IgnoreNullValues = true,
                },
                //Serializer = new CosmosDefaultJsonSerializer(new JsonSerializerSettings
                //{
                //    NullValueHandling = NullValueHandling.Ignore,
                //    ContractResolver = new CamelCasePropertyNamesContractResolver
                //    {
                //        NamingStrategy = new CamelCaseNamingStrategy(false, true, false),
                //    },
                //}),
            };

            // The emulator: its certificate is self-signed, and "localhost" must connect over IPv4 first, or every new
            // pooled connection waits about 2 s for the refused IPv6 attempt (EmulatorHttpHandler). The SDK sets its
            // timeout and headers on the HttpClient it gets and disposes it with the CosmosClient, so each call gets a
            // new client over the one handler, which this provider disposes.
            if (_options.ShouldAcceptAnyCertificate ?? false)
            {
                var handler = _emulatorHandler = EmulatorHttpHandler.Create();
                options.HttpClientFactory = () => new HttpClient(handler, disposeHandler: false);
                options.ConnectionMode = ConnectionMode.Gateway;
                options.LimitToEndpoint = true;
            }

            foreach (var handler in _handlers)
            {
                options.CustomHandlers.Add(handler);
            }

            CosmosClient client = new(
                connectionString: _options.Connection,
                clientOptions: options);

            return client;
        }
    }
}
