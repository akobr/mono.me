using System;
using System.Collections.Generic;

namespace _42.Platform.Storyteller;

// The meters of one project. Keys are counted at the daily snapshot, operations as they happen.
public static class KeyValueUsageMeters
{
    public const string KeysPublic = "keys.public";
    public const string KeysReference = "keys.reference";
    public const string KeysStandard = "keys.standard";
    public const string KeysHsm = "keys.hsm";
    public const string KeysByok = "keys.byok";
    public const string KeysHyok = "keys.hyok";
    public const string Versions = "versions";
    public const string StorageBytes = "storage.bytes";
    public const string ReadsBinding = "reads.binding";
    public const string ReadsApi = "reads.api";
    public const string Reveals = "reveals";
    public const string Writes = "writes";
    public const string KmsStandard = "kms.standard";
    public const string KmsHsm = "kms.hsm";
    public const string KmsByok = "kms.byok";
    public const string HsmKeyVersions = "hsm.keyVersions";

    // Gauges come from the daily snapshot; everything else is a counter.
    public static readonly IReadOnlySet<string> Gauges = new HashSet<string>(StringComparer.Ordinal)
    {
        KeysPublic, KeysReference, KeysStandard, KeysHsm, KeysByok, KeysHyok, Versions, StorageBytes, HsmKeyVersions,
    };

    public static string KeysOf(KeyValueProtection protection) => protection switch
    {
        KeyValueProtection.Public => KeysPublic,
        KeyValueProtection.Standard => KeysStandard,
        KeyValueProtection.Hsm => KeysHsm,
        KeyValueProtection.Byok => KeysByok,
        KeyValueProtection.Hyok => KeysHyok,
        _ => throw new ArgumentOutOfRangeException(nameof(protection), protection, null),
    };

    public static string KmsOf(KeyValueProtection protection) => protection switch
    {
        KeyValueProtection.Standard => KmsStandard,
        KeyValueProtection.Hsm => KmsHsm,
        KeyValueProtection.Byok => KmsByok,
        _ => throw new ArgumentOutOfRangeException(nameof(protection), protection, null),
    };
}

public record class KeyValueUsageDay
{
    public required DateOnly Date { get; init; }

    public required IReadOnlyDictionary<string, long> Values { get; init; }
}

public record class KeyValueUsageReport
{
    public required DateOnly From { get; init; }

    public required DateOnly To { get; init; }

    // Meter totals over the range; key meters as key-months (the sum of the daily counts divided by the days of the range's months).
    public required IReadOnlyDictionary<string, decimal> Totals { get; init; }

    public required IReadOnlyList<KeyValueUsageDay> Days { get; init; }

    // Only when KeyValues:Pricing is configured: priced item -> amount.
    public IReadOnlyDictionary<string, decimal>? EstimatedCharges { get; init; }
}
