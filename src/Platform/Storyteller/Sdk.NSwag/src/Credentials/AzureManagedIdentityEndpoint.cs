using System;
using System.Net;
using System.Net.Sockets;

namespace _42.Platform.Storyteller.Sdk.Credentials;

// Where the managed identity of an Azure host answers, and the header its request carries: IDENTITY_ENDPOINT with
// IDENTITY_HEADER (App Service, Functions, Container Apps), otherwise IMDS (virtual machines and scale sets). Read from
// the environment before every exchange, not once: the platform rotates the header value.
internal sealed class AzureManagedIdentityEndpoint
{
    // IMDS, the same literal on every Azure virtual machine: a link-local address, which no router forwards.
    private const string ImdsUrl = "http://169.254.169.254/metadata/identity/oauth2/token";
    private const string ImdsApiVersion = "2018-02-01";
    private const string ImdsHeaderName = "Metadata";
    private const string ImdsHeaderValue = "true";
    private const string AppServiceApiVersion = "2019-08-01";
    private const string AppServiceHeaderName = "X-IDENTITY-HEADER";

    // Set by hosts whose managed identity answers another protocol: Service Fabric (with the endpoint and the header),
    // and Azure Arc (with the endpoint, without the header).
    private const string ServiceFabricThumbprint = "IDENTITY_SERVER_THUMBPRINT";
    private const string ArcImdsEndpoint = "IMDS_ENDPOINT";

    private static readonly Uri Imds = new(ImdsUrl, UriKind.Absolute);

    private AzureManagedIdentityEndpoint(Uri url, string apiVersion, string headerName, string headerValue, bool isImds)
    {
        Url = url;
        ApiVersion = apiVersion;
        HeaderName = headerName;
        HeaderValue = headerValue;
        IsImds = isImds;
    }

    public Uri Url { get; }

    public string ApiVersion { get; }

    public string HeaderName { get; }

    // IDENTITY_HEADER is a secret of the host: it goes to Url only, and is in no message.
    public string HeaderValue { get; }

    public bool IsImds { get; }

    // StorytellerAuthenticationException (Configuration) for an IDENTITY_ENDPOINT the rule below refuses, and for a host
    // the two protocols do not cover, which a SubjectTokenProvider serves. Messages name variables, never a value.
    public static AzureManagedIdentityEndpoint Read(Func<string, string?> getVariable)
    {
        var variables = new StorytellerVariableReader(getVariable);
        var endpoint = variables.Read(StorytellerVariables.IDENTITY_ENDPOINT);
        var header = variables.Read(StorytellerVariables.IDENTITY_HEADER);

        if (endpoint is null || header is null)
        {
            if (endpoint is not null && variables.Read(ArcImdsEndpoint) is not null)
            {
                throw NotCovered("Azure Arc", $"{ArcImdsEndpoint} is set with {StorytellerVariables.IDENTITY_ENDPOINT} and without {StorytellerVariables.IDENTITY_HEADER}");
            }

            return new AzureManagedIdentityEndpoint(Imds, ImdsApiVersion, ImdsHeaderName, ImdsHeaderValue, true);
        }

        if (variables.Read(ServiceFabricThumbprint) is not null)
        {
            throw NotCovered("Service Fabric", $"{ServiceFabricThumbprint} is set");
        }

        return Uri.TryCreate(endpoint, UriKind.Absolute, out var url) && IsAllowed(url)
            ? new AzureManagedIdentityEndpoint(url, AppServiceApiVersion, AppServiceHeaderName, header, false)
            : throw StorytellerAuthenticationException.Configuration(
                $"{StorytellerVariables.IDENTITY_ENDPOINT} must be an absolute URL without user info: https, or http to localhost or to a loopback, link-local or private address.");
    }

    // The header value is a secret.
    public override string ToString()
    {
        return $"{nameof(AzureManagedIdentityEndpoint)} {{ Url = {StorytellerUrlRule.Describe(Url)}, IsImds = {IsImds} }}";
    }

    // The rule of IDENTITY_ENDPOINT, wider than StorytellerUrlRule: the platform sets it to http on an address of the
    // host's own network, 127.0.0.1 on Windows App Service, localhost on Container Apps and a link-local address on Linux
    // App Service. So https, or http to localhost or to an address literal that is loopback, link-local (169.254.0.0/16,
    // fe80::/10) or private (10.0.0.0/8, 172.16.0.0/12, 192.168.0.0/16, fc00::/7); never http to a name that DNS
    // resolves, and no user info.
    internal static bool IsAllowed(Uri uri)
    {
        if (!uri.IsAbsoluteUri || !string.IsNullOrEmpty(uri.UserInfo))
        {
            return false;
        }

        if (uri.Scheme == Uri.UriSchemeHttps || StorytellerUrlRule.IsLocalHttp(uri))
        {
            return true;
        }

        return uri.Scheme == Uri.UriSchemeHttp
            && uri.HostNameType is UriHostNameType.IPv4 or UriHostNameType.IPv6
            && IPAddress.TryParse(uri.DnsSafeHost, out var address)
            && IsOfTheHostNetwork(address);
    }

    private static bool IsOfTheHostNetwork(IPAddress address)
    {
        if (address.IsIPv4MappedToIPv6)
        {
            address = address.MapToIPv4();
        }

        if (address.AddressFamily == AddressFamily.InterNetworkV6)
        {
            return address.IsIPv6LinkLocal || address.IsIPv6UniqueLocal;
        }

        var bytes = address.GetAddressBytes();

        return bytes[0] == 10
            || (bytes[0] == 172 && bytes[1] is >= 16 and <= 31)
            || (bytes[0] == 192 && bytes[1] == 168)
            || (bytes[0] == 169 && bytes[1] == 254);
    }

    private static StorytellerAuthenticationException NotCovered(string host, string evidence)
    {
        return StorytellerAuthenticationException.Configuration(
            $"The built-in managed identity source covers App Service, Functions, Container Apps and IMDS, not {host} ({evidence}); get the platform token in code with {nameof(FederatedTokenOptions)}.{nameof(FederatedTokenOptions.SubjectTokenProvider)}, for example with Azure.Identity.");
    }
}
