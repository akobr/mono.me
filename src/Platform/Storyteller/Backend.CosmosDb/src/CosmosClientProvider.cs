using System;
using System.Net.Http;
using Microsoft.Azure.Cosmos;
using Microsoft.Extensions.Options;

namespace _42.Platform.Storyteller
{
    public class CosmosClientProvider : IDisposable, ICosmosClientProvider
    {
        private readonly Lazy<CosmosClient> _client;
        private readonly CosmosDbOptions _options;
        private SocketsHttpHandler? _emulatorHandler;

        public CosmosClientProvider(
            IOptions<CosmosDbOptions> options)
        {
            _client = new Lazy<CosmosClient>(BuildClient);
            _options = options.Value;
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

            CosmosClient client = new(
                connectionString: _options.Connection,
                clientOptions: options);

            return client;
        }
    }
}
