using System.Text;

namespace _42.Platform.Storyteller;

public static class DiffFormatter
{
    public static string ToUnifiedDiff(DiffResult diff)
    {
        var sb = new StringBuilder();

        foreach (var hunk in diff.Hunks)
        {
            sb.AppendLine($"@@ -{hunk.OldStart},{hunk.OldCount} +{hunk.NewStart},{hunk.NewCount} @@");

            foreach (var line in hunk.Lines)
            {
                var prefix = line.Type switch
                {
                    DiffChangeType.Addition => "+",
                    DiffChangeType.Deletion => "-",
                    _ => " ",
                };

                sb.AppendLine($"{prefix}{line.Content}");
            }
        }

        return sb.ToString();
    }
}
