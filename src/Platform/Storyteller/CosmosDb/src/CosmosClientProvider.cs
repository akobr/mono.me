using System;
using System.Net.Http;
using Microsoft.Azure.Cosmos;
using Microsoft.Extensions.Options;
using Newtonsoft.Json;
using Newtonsoft.Json.Serialization;

namespace _42.Platform.Storyteller
{
    public class CosmosClientProvider : IDisposable, ICosmosClientProvider
    {
        private readonly Lazy<CosmosClient> _client;
        private readonly CosmosDbOptions _options;

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
        }

        private CosmosClient BuildClient()
        {
            CosmosClientOptions options = new()
            {
                //SerializerOptions = new()
                //{
                //    PropertyNamingPolicy = CosmosPropertyNamingPolicy.CamelCase,
                //    IgnoreNullValues = true,
                //},
                Serializer = new CosmosDefaultJsonSerializer(new JsonSerializerSettings
                {
                    NullValueHandling = NullValueHandling.Ignore,
                    ContractResolver = new CamelCasePropertyNamesContractResolver
                    {
                        NamingStrategy = new CamelCaseNamingStrategy(false, true, false),
                    },
                }),
            };

            // Needed when certificate on server is not valid
            if (_options.ShouldAcceptAnyCertificate ?? false)
            {
                options.HttpClientFactory = () => new HttpClient(new HttpClientHandler
                {
                    ServerCertificateCustomValidationCallback = HttpClientHandler.DangerousAcceptAnyServerCertificateValidator,
                });
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
