using System.Net;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using _42.Platform.Storyteller.Sdk.Configuring.Settings;
using _42.Platform.Storyteller.Sdk.Configuring.Transport;

namespace _42.Platform.Storyteller.Sdk.Configuring.UnitTests.Transport;

public class TransportFactoryTests
{
    private readonly StubHttpHandler _http = new();

    private readonly Dictionary<string, string?> _environment = new()
    {
        ["STORYTELLER_ENDPOINT"] = TestDocuments.Endpoint,
        ["STORYTELLER_KEY"] = TestDocuments.Key,
    };

    [Fact]
    public async Task TheCredential_SetsAuthorization_AndNoAccessTokenFactoryDoes()
    {
        _http.Respond(HttpStatusCode.OK, TestDocuments.Json(), ("ETag", TestDocuments.WeakTag));
        using var transport = Create(options => options.Credential = TestCredential.Bearer("t0ken"));

        var response = await transport.Client.GetResolvedConfigurationConditionalAsync("acme", "billing", "default", "sbt.northwind", null);

        response.ETag.ShouldBe(TestDocuments.WeakTag);
        var request = _http.Requests.ShouldHaveSingleItem();
        request.Authorization.ShouldBe("Bearer t0ken");
        request.Uri.ShouldBe(new Uri("https://storyteller.example.com/api/v1/acme/billing/default/configuration/sbt.northwind/resolved"));
        request.Headers.ContainsKey(TransportFactory.LocalCertificateHeaderName).ShouldBeFalse();
    }

    [Fact]
    public async Task WithoutACredential_RequestsAreAnonymous()
    {
        _http.Respond(HttpStatusCode.OK, TestDocuments.Json());
        using var transport = Create();

        await transport.Client.GetConfigurationConditionalAsync("acme", "billing", "default", "sbt.northwind", null);

        _http.Requests.ShouldHaveSingleItem().Authorization.ShouldBeNull();
    }

    [Fact]
    public async Task GetRequests_AreRetried()
    {
        _http.Respond(HttpStatusCode.ServiceUnavailable);
        _http.Respond(HttpStatusCode.OK, TestDocuments.Json());
        using var transport = Create();

        var response = await transport.Client.GetConfigurationConditionalAsync("acme", "billing", "default", "sbt.northwind", null);

        response.StatusCode.ShouldBe(200);
        _http.Requests.Count.ShouldBe(2);
    }

    [Fact]
    public async Task ACertificate_OverLocalHttp_GoesInTheHeaderOfALocalHost()
    {
        using var certificate = CreateCertificate();
        _environment["STORYTELLER_ENDPOINT"] = "http://localhost:7071/api";
        _http.Respond(HttpStatusCode.OK, TestDocuments.Json());
        using var transport = Create(options => options.Credential = TestCredential.Bearer("t0ken", certificate));

        await transport.Client.GetConfigurationConditionalAsync("acme", "billing", "default", "sbt.northwind", null);

        _http.Requests.ShouldHaveSingleItem().Headers[TransportFactory.LocalCertificateHeaderName].ShouldBe(Convert.ToBase64String(certificate.RawData));
    }

    [Fact]
    public async Task ACertificate_OverHttps_GoesInTheHandshakeOnly()
    {
        using var certificate = CreateCertificate();
        _http.Respond(HttpStatusCode.OK, TestDocuments.Json());
        using var transport = Create(options => options.Credential = TestCredential.Bearer("t0ken", certificate));

        await transport.Client.GetConfigurationConditionalAsync("acme", "billing", "default", "sbt.northwind", null);

        _http.Requests.ShouldHaveSingleItem().Headers.ContainsKey(TransportFactory.LocalCertificateHeaderName).ShouldBeFalse();
    }

    [Fact]
    public void ThePrimaryHandler_KeepsTheCertificateAndNoRedirects_WhateverTheCallbackDid()
    {
        using var certificate = CreateCertificate();

        using var handler = TransportFactory.CreatePrimaryHandler(
            primary =>
            {
                primary.AllowAutoRedirect = true;
                primary.SslOptions = new System.Net.Security.SslClientAuthenticationOptions();
                primary.Proxy = new WebProxy("http://proxy.example.com:3128");
            },
            certificate);

        handler.AllowAutoRedirect.ShouldBeFalse();
        handler.PooledConnectionLifetime.ShouldBe(TimeSpan.FromMinutes(5));
        handler.Proxy.ShouldNotBeNull();
        handler.SslOptions.ClientCertificates.ShouldNotBeNull().Cast<X509Certificate>().ShouldHaveSingleItem().ShouldBeSameAs(certificate);
    }

    [Fact]
    public void Disposing_LeavesTheHandlerOfTheTest()
    {
        Create().Dispose();

        _http.IsDisposed.ShouldBeFalse();
    }

    private static X509Certificate2 CreateCertificate()
    {
        using var key = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var request = new CertificateRequest("CN=storyteller-configuration-test", key, HashAlgorithmName.SHA256);
        return request.CreateSelfSigned(DateTimeOffset.UtcNow.AddMinutes(-5), DateTimeOffset.UtcNow.AddDays(1));
    }

    private StorytellerTransport Create(Action<StorytellerConfigurationOptions>? configure = null)
    {
        var options = TestOptions.Create(_environment, _http);
        configure?.Invoke(options);
        return TransportFactory.Create(SettingsReader.Read(options, true));
    }
}
