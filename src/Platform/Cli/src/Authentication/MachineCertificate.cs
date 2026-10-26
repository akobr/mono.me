using System;
using System.Collections.Generic;
using System.Formats.Asn1;
using System.Linq;
using System.Security.Cryptography.X509Certificates;
using System.Text;

namespace _42.Platform.Cli.Authentication;

internal static class MachineCertificate
{
    private const string SpiffeScheme = "spiffe";
    private const int UriGeneralNameTag = 6;

    // The identity in the URI SAN of a machine or shared certificate; the SDK loads the file
    // (StorytellerClientCertificate.Load).
    public static MachineCertificateIdentity? ReadIdentity(X509Certificate2 certificate)
    {
        var platformUris = certificate.Extensions
            .OfType<X509SubjectAlternativeNameExtension>()
            .SelectMany(extension => ReadUris(extension.RawData))
            .Select(value => Uri.TryCreate(value, UriKind.Absolute, out var uri) ? uri : null)
            .Where(uri => uri is not null && string.Equals(uri.Scheme, SpiffeScheme, StringComparison.OrdinalIgnoreCase))
            .ToList();

        if (platformUris.Count != 1)
        {
            return null;
        }

        // org/{organization}/project/{project}/machine/{id} or org/{organization}/project/{project}/shared/{label}
        var segments = platformUris[0]!.AbsolutePath.Split('/', StringSplitOptions.RemoveEmptyEntries);

        if (segments.Length != 6
            || !string.Equals(segments[0], "org", StringComparison.OrdinalIgnoreCase)
            || !string.Equals(segments[2], "project", StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        return segments[4].ToLowerInvariant() switch
        {
            "machine" => new MachineCertificateIdentity(segments[1], segments[3], segments[5], null),
            "shared" => new MachineCertificateIdentity(segments[1], segments[3], null, segments[5]),
            _ => null,
        };
    }

    private static List<string> ReadUris(byte[] subjectAlternativeName)
    {
        // SubjectAltName ::= SEQUENCE OF GeneralName; uniformResourceIdentifier is [6] IMPLICIT IA5String.
        var uris = new List<string>();

        try
        {
            var sequence = new AsnReader(subjectAlternativeName, AsnEncodingRules.DER).ReadSequence();

            while (sequence.HasData)
            {
                var tag = sequence.PeekTag();

                if (tag.TagClass == TagClass.ContextSpecific && tag.TagValue == UriGeneralNameTag)
                {
                    uris.Add(Encoding.ASCII.GetString(sequence.ReadOctetString(tag)));
                }
                else
                {
                    sequence.ReadEncodedValue();
                }
            }
        }
        catch (AsnContentException)
        {
            // A damaged extension carries no identity.
        }

        return uris;
    }
}
