using System;
using System.Net;
using System.Net.Http;
using System.Threading.Tasks;
using _42.Platform.Cli.UnitTests.Authentication;
using _42.Platform.Storyteller.Sdk;
using Shouldly;
using Xunit;

namespace _42.Platform.Cli.UnitTests.Sdk;

// Sdk.NSwag has no test project of its own; sform is its only consumer.
public class SdkCertificateHeaderTests
{
    private const string HeaderName = "X-ARR-ClientCert";

    private readonly ScriptedHttpHandler _http = new();

    [Fact]
    public async Task CertificateWithAHeaderName_IsSentEvenWithoutAToken()
    {
        using var certificate = TestCertificates.Load(TestCertificates.Pkcs12(TestCertificates.MachineUri("acme", "billing", "3f2c")));

        await CallAsync(new SdkConfiguration { ClientCertificate = certificate, ClientCertificateHeaderName = HeaderName, AccessTokenFactory = () => string.Empty });

        var request = _http.Requests.ShouldHaveSingleItem();
        request.Authorization.ShouldBeNull();
        request.Headers![HeaderName].ShouldBe(Convert.ToBase64String(certificate.RawData));
    }

    [Fact]
    public async Task CertificateWithAHeaderName_GoesWithTheApiKey()
    {
        using var certificate = TestCertificates.Load(TestCertificates.Pkcs12(TestCertificates.MachineUri("acme", "billing", "3f2c")));

        await CallAsync(new SdkConfiguration { ClientCertificate = certificate, ClientCertificateHeaderName = HeaderName, AccessTokenFactory = () => "ApiKey 2s.a.b" });

        var request = _http.Requests.ShouldHaveSingleItem();
        request.Authorization.ShouldBe("ApiKey 2s.a.b");
        request.Headers!.ShouldContainKey(HeaderName);
    }

    [Fact]
    public async Task WithoutAHeaderName_TheCertificateStaysInTheHandshake()
    {
        using var certificate = TestCertificates.Load(TestCertificates.Pkcs12(TestCertificates.MachineUri("acme", "billing", "3f2c")));

        await CallAsync(new SdkConfiguration { ClientCertificate = certificate, AccessTokenFactory = () => "jwt" });

        var request = _http.Requests.ShouldHaveSingleItem();
        request.Authorization.ShouldBe("Bearer jwt");
        request.Headers!.ShouldNotContainKey(HeaderName);
    }

    [Fact]
    public async Task HeaderNameWithoutACertificate_SendsNothing()
    {
        await CallAsync(new SdkConfiguration { ClientCertificateHeaderName = HeaderName });

        _http.Requests.ShouldHaveSingleItem().Headers!.ShouldNotContainKey(HeaderName);
    }

    private async Task CallAsync(SdkConfiguration configuration)
    {
        configuration.BaseUrl = "http://localhost:7071/api";
        _http.Respond(HttpStatusCode.NotFound, string.Empty);
        var client = new AccessApiClient(new HttpClient(_http), configuration);

        await Should.ThrowAsync<ApiException>(() => client.GetAccountAsync());
    }
}
