using System;
using System.Net.Http;
using Microsoft.Azure.Cosmos;

namespace _42.Platform.Storyteller
{
    public class CosmosClientProvider : IDisposable, ICosmosClientProvider
    {
        private readonly Lazy<CosmosClient> _client;

        public CosmosClientProvider()
        {
            _client = new Lazy<CosmosClient>(BuildClient);
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
                SerializerOptions = new()
                {
                    PropertyNamingPolicy = CosmosPropertyNamingPolicy.CamelCase,
                    IgnoreNullValues = true,
                },

                // Needed when certificate on server is not valid
                HttpClientFactory = () => new HttpClient(new HttpClientHandler
                {
                    ServerCertificateCustomValidationCallback = HttpClientHandler.DangerousAcceptAnyServerCertificateValidator,
                }),
                ConnectionMode = ConnectionMode.Gateway,
                LimitToEndpoint = true,
            };

            CosmosClient client = new(
                accountEndpoint: "https://localhost:8081/",
                authKeyOrResourceToken: "C2y6yDjf5/R+ob0N8A7Cgv30VRDJIWEHLM+4QDU5DE2nQ9nDuVTqobD4b8mGGyPMbIZnqyMsEcaGQy67XIw/Jw==",
                clientOptions: options);

            return client;
        }
    }
}
