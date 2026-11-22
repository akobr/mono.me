#nullable enable

using System;
using System.Threading;
using System.Threading.Tasks;
using DotNet.Testcontainers.Builders;
using DotNet.Testcontainers.Containers;
using Testcontainers.CosmosDb;

namespace _42.Platform.Storyteller.Backend.CosmosDb.UnitTests;

// Which Cosmos DB emulator a test run uses.
public enum CosmosEmulatorKind
{
    // The Windows emulator installed on the machine (https://localhost:8081), without STORYTELLER_TESTS_USE_CONTAINER.
    Local,

    // The Linux vNext emulator in a container: the default with STORYTELLER_TESTS_USE_CONTAINER=true. No request
    // charges (a flat 1 RU), no range or composite indexes, no paging of an ORDER BY query from a Linux client (one page, whatever its
    // MaxItemCount), and no expiry of items within a test's wait; it stores and applies replaced container settings (TTL,
    // indexing policy).
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

    // The trait of the tests that only the engine of the service answers right: request charges, indexes, stored sizes, TTL expiry, paging.
    public const string TraitName = "Emulator";
    public const string ClassicTrait = "Classic";

    // Pinned by digest to the releases the suites ran green on, so a new emulator release never changes a run by itself:
    // a tag of this registry can move (vnext-EN20261019 was pointed at the hotfix build 20261120.1 on 2026-11-21, which
    // answers a duplicate create with 500 "cannot drop portal" instead of 409, Azure/azure-cosmos-db-emulator-docker#391).
    // The tag stays for the reader. Move both on purpose, run the suites on them, and for vNext check that the new image
    // keeps its start-up package update off (see BuildVNext).
    private const string VNextImage = "mcr.microsoft.com/cosmosdb/linux/azure-cosmos-emulator:vnext-EN20261019@sha256:db53613dae19d0a3d5ad50688d14aa6e957af082dafea56b52e1bd3a6b3f1406";
    private const string ClassicImage = "mcr.microsoft.com/cosmosdb/linux/azure-cosmos-emulator:2.14.28@sha256:9a4f69121f20c1d6502db84625c71ebb441ab61b01880a7b2d3d8031c6dac429";
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

    public static string SkipReasonOnVNext => "The vNext emulator has no request charges, no range indexes, does not expire items in time, and does not keep the page size of every query; run with STORYTELLER_TESTS_EMULATOR=classic or the local emulator.";

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
    // Its packages stay the ones of the image. The pinned image runs "tdnf distro-sync" in the background on every start,
    // which since PostgreSQL 16.16 (2026-11-20) replaces the libraries of the running PostgreSQL 16.15 about a minute after
    // the start: from then on some requests fail with 500 "PostgresError(SqlState(E58P01))" (a DELETE or replace of a
    // container) or "cannot drop portal" (Azure/azure-cosmos-db-emulator-docker#390). The update cannot reach the package
    // repository with packages.microsoft.com on 127.0.0.1, and fails fast; ENABLE_PACKAGE_UPDATE=false is the switch of the
    // later images (off by default there), for when the pin moves. The suites retry no 500 of the emulator: a new one fails
    // the test, so a red run shows the next regression of the emulator instead of hiding it.
    private static IContainer BuildVNext(string reuseLabel)
    {
        return new ContainerBuilder(VNextImage)
            .WithCommand("--protocol", "https")
            .WithExtraHost("packages.microsoft.com", "127.0.0.1")
            .WithEnvironment("ENABLE_PACKAGE_UPDATE", "false")
            .WithPortBinding(GatewayPort, true)
            .WithPortBinding(HealthPort, true)
            .WithReuse(true)
            .WithLabel("net.42for.tests.cosmos", $"{reuseLabel}-vnext")
            .WithWaitStrategy(Wait.ForUnixContainer().UntilHttpRequestIsSucceeded(request => request
                .ForPort(HealthPort)
                .ForPath("/ready")))
            .Build();
    }

    // 10 partitions, the emulator's default, named so nobody lowers it: fewer run out of partitions (503, substatus 1007).
    // The port opens before the partitions start: wait for the emulator's "Started" line, or the first requests fail
    // with "The SSL connection could not be established".
    private static IContainer BuildClassic(string reuseLabel)
    {
        return new CosmosDbBuilder(ClassicImage)
            .WithReuse(true)
            .WithLabel("net.42for.tests.cosmos", reuseLabel)
            .WithEnvironment("AZURE_COSMOS_EMULATOR_PARTITION_COUNT", "10")
            .WithWaitStrategy(Wait.ForUnixContainer().UntilMessageIsLogged(@"(?m)^Started\s*$"))
            .Build();
    }
}
