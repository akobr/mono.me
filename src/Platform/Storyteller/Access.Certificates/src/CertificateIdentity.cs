using System.Formats.Asn1;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using System.Text.RegularExpressions;
using _42.Platform.Storyteller.Accessing.Model;

namespace _42.Platform.Storyteller;

public record CertificateIdentity(
    string Organization,
    string Project,
    string? MachineAccessId,
    string? SharedLabel,
    ClientCertificateKind Kind)
{
    private static readonly Regex SpiffePathSegmentRegex = new(@"^[a-zA-Z0-9._-]+$", RegexOptions.Compiled);

    public static CertificateIdentity? TryParse(X509Certificate2 certificate, string trustDomain)
    {
        var sanExtension = certificate.Extensions.OfType<X509SubjectAlternativeNameExtension>().FirstOrDefault();

        if (sanExtension is null)
        {
            return null;
        }

        var uris = ExtractUriSans(sanExtension);
        var platformUris = uris
            .Where(u => u.StartsWith($"spiffe://{trustDomain}/", StringComparison.OrdinalIgnoreCase))
            .ToList();

        if (platformUris.Count != 1)
        {
            return null;
        }

        var uri = new Uri(platformUris[0]);
        var segments = uri.AbsolutePath.Split('/', StringSplitOptions.RemoveEmptyEntries);

        // Expected: org/{organization}/project/{project}/machine/{machineAccessId}
        // or:       org/{organization}/project/{project}/shared/{label}
        if (segments.Length != 6
            || !string.Equals(segments[0], "org", StringComparison.OrdinalIgnoreCase)
            || !string.Equals(segments[2], "project", StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        var organization = segments[1];
        var project = segments[3];
        var kindSegment = segments[4];
        var idOrLabel = segments[5];

        if (string.Equals(kindSegment, "machine", StringComparison.OrdinalIgnoreCase))
        {
            return new CertificateIdentity(organization, project, idOrLabel, null, ClientCertificateKind.Machine);
        }

        if (string.Equals(kindSegment, "shared", StringComparison.OrdinalIgnoreCase))
        {
            return new CertificateIdentity(organization, project, null, idOrLabel, ClientCertificateKind.Shared);
        }

        return null;
    }

    public string ToSpiffeUri(string trustDomain)
    {
        return Kind switch
        {
            ClientCertificateKind.Machine => $"spiffe://{trustDomain}/org/{Organization}/project/{Project}/machine/{MachineAccessId}",
            ClientCertificateKind.Shared => $"spiffe://{trustDomain}/org/{Organization}/project/{Project}/shared/{SharedLabel}",
            _ => throw new InvalidOperationException($"Unknown certificate kind: {Kind}"),
        };
    }

    public static bool IsValidSpiffePathSegment(string value)
    {
        return !string.IsNullOrEmpty(value) && SpiffePathSegmentRegex.IsMatch(value);
    }

    private static List<string> ExtractUriSans(X509SubjectAlternativeNameExtension sanExtension)
    {
        // Parse the SAN extension's raw data using ASN.1.
        // SubjectAltName ::= SEQUENCE OF GeneralName
        // GeneralName has tag [6] IMPLICIT for uniformResourceIdentifier (IA5String).
        var uris = new List<string>();

        try
        {
            var reader = new AsnReader(sanExtension.RawData, AsnEncodingRules.DER);
            var sequence = reader.ReadSequence();

            while (sequence.HasData)
            {
                var tag = sequence.PeekTag();

                // Context-specific tag 6 = uniformResourceIdentifier.
                if (tag.TagClass == TagClass.ContextSpecific && tag.TagValue == 6)
                {
                    var uriBytes = sequence.ReadOctetString(tag);
                    var uri = Encoding.ASCII.GetString(uriBytes);
                    uris.Add(uri);
                }
                else
                {
                    // Skip other GeneralName types.
                    sequence.ReadEncodedValue();
                }
            }
        }
        catch
        {
            // If parsing fails, return empty — the certificate has no valid URIs.
        }

        return uris;
    }
}
