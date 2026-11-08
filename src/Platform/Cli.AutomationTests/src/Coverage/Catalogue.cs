using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace _42.Platform.Cli.AutomationTests.Coverage;

// docs/Platform/Cli.AutomationTests/test-cases.md parsed: the sections with their folders (the "Cases per section" table),
// and every case row of the section tables: Reference, Testiny id, title, priority, role, modes, Covers and the planned test
// method, with the class of its sub-folder heading ("### Device Code (`DeviceCodeSignInTests`)").
public sealed partial class Catalogue
{
    public const string RelativePath = "docs/Platform/Cli.AutomationTests/test-cases.md";

    public static readonly IReadOnlyList<string> Priorities = ["Elementary", "High", "Medium", "Low"];

    public static readonly IReadOnlyList<string> Roles = ["Any", "Reader", "Contributor", "Editor", "Secrets", "Admin", "Owner", "Machine"];

    public static readonly IReadOnlyList<string> PlainModes = ["U", "F", "M", "T", "E", "P"];

    private Catalogue(IReadOnlyList<CatalogueSection> sections, IReadOnlyList<CatalogueCase> cases, IReadOnlyList<string> problems)
    {
        Sections = sections;
        Cases = cases;
        Problems = problems;
    }

    public IReadOnlyList<CatalogueSection> Sections { get; }

    public IReadOnlyList<CatalogueCase> Cases { get; }

    // What does not parse: a row with another number of cells, a row outside a section or a sub-folder heading.
    public IReadOnlyList<string> Problems { get; }

    public static Catalogue Parse(string text)
    {
        ArgumentNullException.ThrowIfNull(text);

        var sections = new List<CatalogueSection>();
        var cases = new List<CatalogueCase>();
        var problems = new List<string>();
        CatalogueSection? section = null;
        string? className = null;
        string? subFolder = null;

        var lines = text.Replace("\r\n", "\n", StringComparison.Ordinal).Split('\n');
        for (var index = 0; index < lines.Length; index++)
        {
            var line = lines[index];
            var number = index + 1;

            if (SectionRow().Match(line) is { Success: true } sectionRow)
            {
                sections.Add(new CatalogueSection(sectionRow.Groups["name"].Value.Trim(), sectionRow.Groups["folder"].Value.Trim(), int.Parse(sectionRow.Groups["count"].Value, CultureInfo.InvariantCulture)));
                continue;
            }

            if (line.StartsWith("## ", StringComparison.Ordinal))
            {
                var name = line[3..].Trim();
                section = sections.FirstOrDefault(known => known.Name == name);
                className = null;
                subFolder = null;
                continue;
            }

            if (line.StartsWith("### ", StringComparison.Ordinal))
            {
                var heading = ClassHeading().Match(line);
                className = heading.Success ? heading.Groups["class"].Value : null;
                subFolder = heading.Success ? heading.Groups["title"].Value.Trim() : line[4..].Trim();
                if (section is not null && !heading.Success)
                {
                    problems.Add($"line {number}: the sub-folder heading '{line.Trim()}' names no test class (\"### <Title> (`<Class>`)\").");
                }

                continue;
            }

            if (!CaseRow().IsMatch(line))
            {
                continue;
            }

            var cells = Cells(line);
            if (cells.Count != 8)
            {
                problems.Add($"line {number}: a case row with {cells.Count} cells instead of 8: {line.Trim()}");
                continue;
            }

            if (section is null || className is null)
            {
                problems.Add($"line {number}: {cells[0]} is outside a section of the \"Cases per section\" table or a sub-folder heading with its class.");
                continue;
            }

            cases.Add(new CatalogueCase
            {
                Reference = cells[0],
                Testiny = cells[1],
                Title = cells[2],
                Priority = cells[3],
                Role = cells[4],
                Modes = [.. cells[5].Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)],
                Covers = [.. BacktickedToken().Matches(cells[6]).Select(match => match.Groups[1].Value.Trim())],
                Method = cells[7].Trim('`', ' '),
                Section = section,
                SubFolder = subFolder ?? string.Empty,
                ClassName = className,
                Line = number,
            });
        }

        return new Catalogue(sections, cases, problems);
    }

    // The cells of a table row; a | inside backticks belongs to its cell ("`-k|--credential-kind`").
    private static List<string> Cells(string line)
    {
        var cells = new List<string>();
        var current = new StringBuilder();
        var inCode = false;
        var body = line.Trim();
        body = body.Substring(1, body.Length - (body.EndsWith('|') ? 2 : 1));
        foreach (var character in body)
        {
            if (character == '`')
            {
                inCode = !inCode;
            }

            if (character == '|' && !inCode)
            {
                cells.Add(current.ToString().Trim());
                current.Clear();
                continue;
            }

            current.Append(character);
        }

        cells.Add(current.ToString().Trim());
        return cells;
    }

    [GeneratedRegex(@"^\|\s*\[(?<name>[^\]]+)\]\(#[^)]*\)\s*\|\s*`(?<folder>Tests/[^`]+)`\s*\|\s*(?<count>\d+)\s*\|\s*$")]
    private static partial Regex SectionRow();

    [GeneratedRegex(@"^###\s+(?<title>.+?)\s+\(`(?<class>[A-Za-z_][A-Za-z0-9_]*)`\)\s*$")]
    private static partial Regex ClassHeading();

    [GeneratedRegex(@"^\|\s*[A-Z]+-[A-Z]+-\d+\s*\|")]
    private static partial Regex CaseRow();

    [GeneratedRegex(@"`([^`]+)`")]
    private static partial Regex BacktickedToken();
}
