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
}
