namespace _42.Platform.Storyteller.Mcp.Hosting;

// Section Mcp:Actor:ClientAssertionKey: a PKCS#12 file with the RSA key that signs RFC 7523 client assertions. The
// server publishes its public key at /.well-known/jwks.json, the JwksUri of the API's ClientAssertion actor.
public sealed class McpClientAssertionKeyOptions
{
    public string? CertificatePath { get; set; }

    public string? CertificatePassword { get; set; }

    // The aud of the client assertions. Empty means {Storyteller:BaseUrl}/v1/access/token. Set it when the server
    // reaches the API by another URL than the one it publishes: to the API's
    // {MachineAuth:Federation:PublicBaseUrl}/v1/access/token or its MachineAuth:Federation:Issuer, exactly as the API
    // has them.
    public string? Audience { get; set; }

    // Set when any value is: the template in appsettings.json lists every key with an empty value.
    internal bool IsSet => !string.IsNullOrWhiteSpace(CertificatePath)
        || !string.IsNullOrWhiteSpace(CertificatePassword)
        || !string.IsNullOrWhiteSpace(Audience);
}
