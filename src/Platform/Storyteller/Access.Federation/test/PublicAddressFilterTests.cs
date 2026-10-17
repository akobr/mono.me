using System.Net;
using _42.Platform.Storyteller.Accessing;

namespace _42.Platform.Storyteller.Access.Federation.UnitTests;

public class PublicAddressFilterTests
{
    [Theory]
    [InlineData("0.0.0.0")]
    [InlineData("0.1.2.3")]
    [InlineData("10.1.2.3")]
    [InlineData("100.64.0.1")]
    [InlineData("100.127.255.254")]
    [InlineData("127.0.0.1")]
    [InlineData("127.255.255.254")]
    [InlineData("169.254.169.254")]
    [InlineData("172.16.0.1")]
    [InlineData("172.31.255.255")]
    [InlineData("192.0.0.170")]
    [InlineData("192.0.2.1")]
    [InlineData("192.88.99.1")]
    [InlineData("192.168.1.1")]
    [InlineData("198.18.0.1")]
    [InlineData("198.19.255.255")]
    [InlineData("198.51.100.7")]
    [InlineData("203.0.113.9")]
    [InlineData("224.0.0.251")]
    [InlineData("239.255.255.250")]
    [InlineData("240.0.0.1")]
    [InlineData("255.255.255.255")]
    public void SpecialPurposeIPv4_IsRefused(string address)
    {
        PublicAddressFilter.IsPublic(IPAddress.Parse(address)).ShouldBeFalse();
    }

    [Theory]
    [InlineData("1.1.1.1")]
    [InlineData("8.8.8.8")]
    [InlineData("20.42.73.1")]
    [InlineData("100.63.255.255")]
    [InlineData("100.128.0.0")]
    [InlineData("172.15.255.255")]
    [InlineData("172.32.0.0")]
    [InlineData("192.0.3.1")]
    [InlineData("198.17.255.255")]
    [InlineData("198.20.0.0")]
    [InlineData("223.255.255.255")]
    public void GlobalIPv4_IsAllowed(string address)
    {
        PublicAddressFilter.IsPublic(IPAddress.Parse(address)).ShouldBeTrue();
    }

    [Theory]
    [InlineData("::")]
    [InlineData("::1")]
    [InlineData("100::1")]
    [InlineData("2001:db8::1")]
    [InlineData("2001:2::1")]
    [InlineData("2001:10::1")]
    [InlineData("3fff::1")]
    [InlineData("5f00::1")]
    [InlineData("fc00::1")]
    [InlineData("fd12:3456::1")]
    [InlineData("fe80::1")]
    [InlineData("fec0::1")]
    [InlineData("ff02::1")]
    [InlineData("64:ff9b:0:0:1::")]
    public void SpecialPurposeIPv6_IsRefused(string address)
    {
        PublicAddressFilter.IsPublic(IPAddress.Parse(address)).ShouldBeFalse();
    }

    [Theory]
    [InlineData("2606:4700:4700::1111")]
    [InlineData("2620:4f:8000::1")]
    [InlineData("2a00:1450:4001:80b::200e")]
    public void GlobalIPv6_IsAllowed(string address)
    {
        PublicAddressFilter.IsPublic(IPAddress.Parse(address)).ShouldBeTrue();
    }

    [Theory]
    [InlineData("::ffff:127.0.0.1", false)]
    [InlineData("::ffff:169.254.169.254", false)]
    [InlineData("::ffff:10.0.0.1", false)]
    [InlineData("::ffff:8.8.8.8", true)]
    [InlineData("::ffff:0:127.0.0.1", false)]
    [InlineData("::ffff:0:8.8.8.8", true)]
    [InlineData("::127.0.0.1", false)]
    [InlineData("::8.8.8.8", true)]
    [InlineData("64:ff9b::127.0.0.1", false)]
    [InlineData("64:ff9b::a9fe:a9fe", false)]
    [InlineData("64:ff9b::8.8.8.8", true)]
    [InlineData("2002:7f00:1::1", false)]
    [InlineData("2002:a9fe:a9fe::1", false)]
    [InlineData("2002:808:808::1", true)]
    public void EmbeddedIPv4_IsCheckedAsIPv4(string address, bool isPublic)
    {
        PublicAddressFilter.IsPublic(IPAddress.Parse(address)).ShouldBe(isPublic);
    }

    // The local-use NAT64 prefix in its /96 form is checked as IPv4; other RFC 6052 lengths are ambiguous and refused.
    [Theory]
    [InlineData("64:ff9b:1::808:808", true)]
    [InlineData("64:ff9b:1::7f00:1", false)]
    [InlineData("64:ff9b:1::a9fe:a9fe", false)]
    [InlineData("64:ff9b:1:7f00:1::", false)]
    [InlineData("64:ff9b:1:808:8:800::", false)]
    [InlineData("64:ff9b:1:0:7f:0:100:0", false)]
    public void LocalUseNat64_ChecksTheUnambiguousForm(string address, bool isPublic)
    {
        PublicAddressFilter.IsPublic(IPAddress.Parse(address)).ShouldBe(isPublic);
    }

    // Teredo: the server follows 2001:0::/32, the client is the inverted last 32 bits.
    [Theory]
    [InlineData("2001:0:808:808:0:0:f7f7:f7f7", true)]
    [InlineData("2001:0:7f00:1:0:0:f7f7:f7f7", false)]
    [InlineData("2001:0:808:808:0:0:80ff:fffe", false)]
    public void Teredo_ChecksTheServerAndTheClient(string address, bool isPublic)
    {
        PublicAddressFilter.IsPublic(IPAddress.Parse(address)).ShouldBe(isPublic);
    }

    [Fact]
    public void Handler_NeverUsesAProxyOrFollowsARedirect_AndConnectsOnlyToPublicAddresses()
    {
        using var handler = FederationMetadataHttpHandler.Create(new FederationOptions());

        handler.UseProxy.ShouldBeFalse();
        handler.AllowAutoRedirect.ShouldBeFalse();
        handler.ConnectCallback.ShouldNotBeNull();
    }

    [Fact]
    public void Handler_WithPrivateNetworksAllowed_ConnectsAnywhere()
    {
        using var handler = FederationMetadataHttpHandler.Create(new FederationOptions { AllowPrivateNetworks = true });

        handler.ConnectCallback.ShouldBeNull();
        handler.AllowAutoRedirect.ShouldBeFalse();
    }

    [Theory]
    [InlineData("https://127.0.0.1/.well-known/openid-configuration")]
    [InlineData("https://[::1]/.well-known/openid-configuration")]
    [InlineData("http://localhost:9/.well-known/openid-configuration")]
    public async Task Handler_RefusesToConnectToAPrivateHost(string url)
    {
        using var client = new HttpClient(FederationMetadataHttpHandler.Create(new FederationOptions()));

        var exception = await Should.ThrowAsync<HttpRequestException>(() => client.GetAsync(url));

        exception.ToString().ShouldContain("has no public address");
    }
}
