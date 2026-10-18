using System.Text;
using _42.Platform.Storyteller.Accessing.Federation;
using _42.Platform.Storyteller.Api.V1;
using _42.Platform.Storyteller.KeyValues;

namespace _42.Platform.Storyteller.Api.Functions.UnitTests;

// Each limit holds the largest body its routes accept where the domain bounds it, serialized as a System.Text.Json
// client does by default: a quote or '<' as \u0022 or \u003C, 6 bytes, the most any encoder takes for one character.
public class RequestBodyLimitsTests
{
    [Fact]
    public void KeyValueWrite_HoldsAClientEncryptedValueAtItsLimit()
    {
        var write = new KeyValueWrite
        {
            Protection = KeyValueProtection.Hyok,
            Value = new string('"', KeyValueRules.MaxClientEncryptedValueLength),
            ContentType = new string('<', KeyValueRules.MaxContentTypeLength),
            ExpectedVersion = ulong.MaxValue,
        };

        Bytes(write).ShouldBeLessThanOrEqualTo(RequestBodyLimits.KeyValueWrite);
    }

    [Fact]
    public void KeyValueBulk_HoldsAHundredKeysAtTheirLimit()
    {
        var bulk = new KeyValueBulkRequest
        {
            Keys = [.. Enumerable.Range(0, 100).Select(index => $"{index}".PadRight(KeyValueRules.MaxKeyLength, 'k'))],
            KeyPrefix = new string('k', KeyValueRules.MaxKeyLength),
            Protection = KeyValueProtection.Standard,
        };

        Bytes(bulk).ShouldBeLessThanOrEqualTo(RequestBodyLimits.KeyValueBulk);
    }

    // The 1,000 items of an import with plain values at their limit; more than that would not pass the 30 MB request
    // limit of Kestrel.
    [Fact]
    public void KeyValueImport_HoldsAThousandPlainValuesAtTheirLimit()
    {
        var value = new string('v', KeyValueRules.MaxValueBytes);
        var document = new KeyValueDocument
        {
            Items =
            [
                .. Enumerable.Range(0, 1000).Select(index => new KeyValueDocumentItem
                {
                    Key = $"{index}".PadRight(KeyValueRules.MaxKeyLength, 'k'),
                    Protection = KeyValueProtection.Standard,
                    Value = value,
                    ContentType = new string('c', KeyValueRules.MaxContentTypeLength),
                }),
            ],
        };

        Bytes(document).ShouldBeLessThanOrEqualTo(RequestBodyLimits.KeyValueImport);
    }

    // A JWKS at its limit alone is past the 32 KiB of the token endpoint, once its quotes are escaped.
    [Fact]
    public void FederatedIssuer_HoldsAJwksAndRequiredClaimsAtTheirLimits()
    {
        var issuer = new FederatedIssuer
        {
            Name = "github",
            Issuer = new string('"', FederationValidation.MaxIssuerLength),
            KeySource = FederatedIssuerKeySource.Jwks,
            Jwks = new string('"', FederationValidation.MaxJwksBytes),
            MaxTokenLifetimeSeconds = FederationValidation.MaxTokenLifetimeSeconds,
            RequiredClaims = ClaimsAtTheirLimits(),
            Description = new string('"', FederationValidation.MaxDescriptionLength),
        };

        var bytes = Bytes(issuer);

        bytes.ShouldBeGreaterThan(FederationHttp.MaxTokenRequestBytes);
        bytes.ShouldBeLessThanOrEqualTo(RequestBodyLimits.FederatedIssuer);
    }

    // The body of the rules PUT; a new machine carries the same list.
    [Fact]
    public void FederationRules_HoldsTenRulesAtTheirLimits()
    {
        List<FederationRule> rules =
        [
            .. Enumerable.Range(0, FederationValidation.MaxRulesPerMachine).Select(_ => new FederationRule
            {
                Issuer = new string('i', 63),
                Audience = new string('"', FederationValidation.MaxAudienceLength),
                Subject = new string('"', FederationValidation.MaxSubjectLength),
                Claims = ClaimsAtTheirLimits(),
            }),
        ];

        Bytes(rules).ShouldBeLessThanOrEqualTo(RequestBodyLimits.FederationRules);
    }

    // A lookup or restore of 100 keys as long as a Cosmos id.
    [Fact]
    public void AnnotationKeys_HoldsAHundredKeysAsLongAsACosmosId()
    {
        var request = new AnnotationRestoreRequest
        {
            Keys = [.. Enumerable.Range(0, 100).Select(index => $"{index}".PadRight(1023, '<'))],
            Descendants = false,
        };

        Bytes(request).ShouldBeLessThanOrEqualTo(RequestBodyLimits.AnnotationKeys);
    }

    // Claim pointers of the longest length, each with the most values of the longest length.
    private static Dictionary<string, IReadOnlyList<string>> ClaimsAtTheirLimits()
    {
        return Enumerable.Range(0, FederationValidation.MaxClaimKeys).ToDictionary(
            index => $"/{index}".PadRight(FederationClaimKeys.MaxPointerLength + 1, '"'),
            _ => (IReadOnlyList<string>)[.. Enumerable.Repeat(new string('"', FederationValidation.MaxClaimValueLength), FederationValidation.MaxClaimValues)]);
    }

    private static int Bytes<T>(T body)
    {
        return Encoding.UTF8.GetByteCount(FunctionTestDoubles.ToJsonBody(body));
    }
}
