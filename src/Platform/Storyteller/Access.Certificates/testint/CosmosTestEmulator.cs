#nullable enable

using System;
using System.Threading;
using System.Threading.Tasks;
using DotNet.Testcontainers.Builders;
using DotNet.Testcontainers.Containers;
using Testcontainers.CosmosDb;

namespace _42.Platform.Storyteller.Access.Certificates.IntegrationTests;

// Which Cosmos DB emulator a test run uses.
public enum CosmosEmulatorKind
{
    // The Windows emulator installed on the machine (https://localhost:8081), without STORYTELLER_TESTS_USE_CONTAINER.
    Local,

    // The Linux vNext emulator in a container: the default with STORYTELLER_TESTS_USE_CONTAINER=true. No request
    // charges (a flat 1 RU), no range or composite indexes, and replacing container settings does nothing.
    VNext,

    // The classic Linux emulator in a container (STORYTELLER_TESTS_EMULATOR=classic): the engine of the service, with
    // real request charges and indexes, x64 only and slow to start.
    Classic,
}

// The emulator of the Cosmos DB tests, picked by STORYTELLER_TESTS_USE_CONTAINER (a container instead of the local
// emulator, whose databases the cleanup deletes) and STORYTELLER_TESTS_EMULATOR (vnext, the default, or classic).
public sealed class CosmosTestEmulator
{
    public const string WellKnownKey = "C2y6yDjf5/R+ob0N8A7Cgv30VRDJIWEHLM+4QDU5DE2nQ9nDuVTqobD4b8mGGyPMbIZnqyMsEcaGQy67XIw/Jw==";

    // The trait of the tests that only the engine of the service answers right: request charges, indexes, container settings.
    public const string TraitName = "Emulator";
    public const string ClassicTrait = "Classic";

    private const string VNextImage = "mcr.microsoft.com/cosmosdb/linux/azure-cosmos-emulator:vnext-latest";
    private const string ClassicImage = "mcr.microsoft.com/cosmosdb/linux/azure-cosmos-emulator:latest";
    private const int GatewayPort = 8081;
    private const int HealthPort = 8080;

    private readonly IContainer? _container;

    private CosmosTestEmulator(CosmosEmulatorKind kind, IContainer? container)
    {
        Kind = kind;
        _container = container;
    }

    public CosmosEmulatorKind Kind { get; }

    // Request charges, indexing policies and container settings behave as in Azure.
    public bool IsLikeTheService => Kind != CosmosEmulatorKind.VNext;

    public static string SkipReasonOnVNext => "The vNext emulator has no request charges, no range indexes, and does not apply container settings; run with STORYTELLER_TESTS_EMULATOR=classic or the local emulator.";

    // reuseLabel keeps one reused emulator per test module: the cleanup deletes the platform database, and Testcontainers'
    // reuse is not safe when two modules start the same container at once (two containers, one hash).
    public static CosmosTestEmulator FromEnvironment(string reuseLabel)
    {
        var useContainer = string.Equals(
            Environment.GetEnvironmentVariable("STORYTELLER_TESTS_USE_CONTAINER"),
            "true",
            StringComparison.OrdinalIgnoreCase);

        if (!useContainer)
        {
            return new CosmosTestEmulator(CosmosEmulatorKind.Local, null);
        }

        var classic = string.Equals(
            Environment.GetEnvironmentVariable("STORYTELLER_TESTS_EMULATOR"),
            "classic",
            StringComparison.OrdinalIgnoreCase);

        return classic
            ? new CosmosTestEmulator(CosmosEmulatorKind.Classic, BuildClassic(reuseLabel))
            : new CosmosTestEmulator(CosmosEmulatorKind.VNext, BuildVNext(reuseLabel));
    }

    public Task StartAsync(CancellationToken cancellationToken)
    {
        return _container?.StartAsync(cancellationToken) ?? Task.CompletedTask;
    }

    // Both container images serve HTTPS on the gateway port; the client accepts their self-signed certificate.
    public string GetConnectionString()
    {
        return _container is null
            ? $"AccountEndpoint=https://localhost:{GatewayPort}/;AccountKey={WellKnownKey}"
            : $"AccountEndpoint=https://{_container.Hostname}:{_container.GetMappedPublicPort(GatewayPort)}/;AccountKey={WellKnownKey}";
    }

    // The vNext emulator through the generic builder: Testcontainers' CosmosDbBuilder (4.16) builds an http:// connection
    // string, waits for the classic log line "Started" and publishes only the gateway port.
    private static IContainer BuildVNext(string reuseLabel)
    {
        return new ContainerBuilder(VNextImage)
            .WithCommand("--protocol", "https")
            .WithPortBinding(GatewayPort, true)
            .WithPortBinding(HealthPort, true)
            .WithReuse(true)
            .WithLabel("net.42for.tests.cosmos", $"{reuseLabel}-vnext")
            .WithWaitStrategy(Wait.ForUnixContainer().UntilHttpRequestIsSucceeded(request => request
                .ForPort(HealthPort)
                .ForPath("/ready")))
            .Build();
    }

    private static IContainer BuildClassic(string reuseLabel)
    {
        return new CosmosDbBuilder(ClassicImage)
            .WithReuse(true)
            .WithLabel("net.42for.tests.cosmos", reuseLabel)
            // 10 partitions, the emulator's default, named so nobody lowers it: fewer run out of partitions (503, substatus 1007).
            .WithEnvironment("AZURE_COSMOS_EMULATOR_PARTITION_COUNT", "10")
            // The port opens before the partitions start: wait for the emulator's "Started" line, or the first requests
            // fail with "The SSL connection could not be established".
            .WithWaitStrategy(Wait.ForUnixContainer().UntilMessageIsLogged(@"(?m)^Started\s*$"))
            .Build();
    }
}
