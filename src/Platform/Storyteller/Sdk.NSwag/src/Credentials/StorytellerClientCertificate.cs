using System;
using System.IO;
using System.IO.Abstractions;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;

namespace _42.Platform.Storyteller.Sdk.Credentials;

public static class StorytellerClientCertificate
{
    // The PKCS#12 file of a machine. Default key storage flags: Windows cannot use an ephemeral key for TLS client
    // authentication.
    public static X509Certificate2 Load(string path, string? password, IFileSystem? fileSystem = null)
    {
        return Load(path, password, fileSystem ?? new FileSystem(), null);
    }

    // source names the variable of the path, for messages; the password is never printed.
    internal static X509Certificate2 Load(string path, string? password, IFileSystem fileSystem, string? source)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);

        var origin = source is null ? string.Empty : $" in {source}";
        byte[] content;

        try
        {
            if (!fileSystem.File.Exists(path))
            {
                throw StorytellerAuthenticationException.Configuration($"The certificate file {path}{origin} does not exist.");
            }

            content = fileSystem.File.ReadAllBytes(path);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or NotSupportedException or ArgumentException)
        {
            throw StorytellerAuthenticationException.Configuration($"The certificate file {path}{origin} could not be read.", exception);
        }

        X509Certificate2 certificate;

        try
        {
            certificate = X509CertificateLoader.LoadPkcs12(content, password);
        }
        catch (CryptographicException exception)
        {
            throw StorytellerAuthenticationException.Configuration(
                $"The certificate file {path}{origin} could not be loaded; check the file and its password.",
                exception);
        }

        if (!certificate.HasPrivateKey)
        {
            certificate.Dispose();
            throw StorytellerAuthenticationException.Configuration(
                $"The certificate in {path}{origin} has no private key; use the .pfx file that sform account machine create wrote.");
        }

        return certificate;
    }
}
