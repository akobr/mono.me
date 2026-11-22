#nullable enable

using System.Net.Http;
using DotNet.Testcontainers.Builders;
using Testcontainers.LowkeyVault;

namespace _42.Platform.Storyteller.Backend.CosmosDb.UnitTests;

// Lowkey Vault in Docker, the Key Vault of the suites that need one: KeyValueEndToEndTests here, and linked into
// DbCreator/testint and KeyValues.Azure.KeyVault/testint. One pinned image, and a readiness check that holds for a
// reused container too.
public static class LowkeyVaultTestContainer
{
    public const string Image = "nagyesta/lowkey-vault:7.3.112";

    // Each suite its own reused vault: disposing a reused container stops it, which would cut off another suite that
    // runs at the same time.
    public const string ReuseLabel = "net.42for.tests.lowkey-vault";

    // The module waits for the log line "Started LowkeyVaultApp". A new instance that finds a stopped reused container
    // reads its logs since the container was created (it has no stop time of its own), finds the line of the previous
    // start at once, and returns while the vault is still starting: the first request then fails with "The SSL
    // connection could not be established". So it also waits for /ping on the vault's HTTPS port, which answers only
    // once the vault serves, after a first start and after a restart alike (Testcontainers runs both waits in order).
    public static LowkeyVaultContainer Build(string suite)
    {
        return new LowkeyVaultBuilder(Image)
            .WithReuse(true)
            .WithLabel(ReuseLabel, suite)
            .WithWaitStrategy(Wait.ForUnixContainer().UntilHttpRequestIsSucceeded(request => request
                .ForPort(LowkeyVaultBuilder.LowkeyVaultPort)
                .ForPath("/ping")
                .UsingTls()
                .UsingHttpMessageHandler(new HttpClientHandler
                {
                    // the vault's self-signed certificate
                    ServerCertificateCustomValidationCallback = HttpClientHandler.DangerousAcceptAnyServerCertificateValidator,
                })))
            .Build();
    }
}
