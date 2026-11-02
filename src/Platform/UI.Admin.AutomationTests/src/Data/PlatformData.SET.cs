using System.Text;

namespace _42.Platform.UI.Admin.AutomationTests.Data;

// The Settings section's generators.
public sealed partial class PlatformData
{
    // A name of exactly length characters without spaces, "at-<runId>-<hex4>-<hex4>…", e.g. a type name over the 40
    // characters a type name keeps: its cut keeps exactly 40, because nothing is left to trim at the end.
    public string UnspacedName(int length)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(length, 1);

        var name = new StringBuilder(RunSlug);
        while (name.Length < length)
        {
            name.Append('-').Append(Data.Hex(HexLength));
        }

        return name.ToString(0, length);
    }

    // The versionless URI of a customer key (BYOK), https://<RunSlug>.vault.azure.net/keys/<word>-<hex4>, without a
    // trailing slash: the run slug is a valid vault name (3 to 24 of a-z, 0-9 and '-'), and a deployed target stores the
    // URI as it parsed it. Nothing verifies the key: the vault does not exist.
    public string CustomerKeyUri()
    {
        return $"https://{RunSlug}.vault.azure.net/keys/{Data.Word()}-{Data.Hex(HexLength)}";
    }

    // A tenant id of a customer key, a new GUID in lower case (as a deployed target stores it).
    public string TenantId()
    {
        return Data.Random.Uuid().ToString("D");
    }
}
