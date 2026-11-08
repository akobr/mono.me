using System.Globalization;

namespace _42.Platform.Cli.AutomationTests.Coverage;

// One row of the catalogue (docs/Platform/Cli.AutomationTests/test-cases.md).
public sealed record CatalogueCase
{
    public required string Reference { get; init; }

    // The Testiny id as written (a number).
    public required string Testiny { get; init; }

    public required string Title { get; init; }

    // Elementary, High, Medium or Low.
    public required string Priority { get; init; }

    // Any, Reader, Contributor, Editor, Secrets, Admin, Owner or Machine.
    public required string Role { get; init; }

    // U, F, M, T, E, P and X:<Feature>; empty for U alone.
    public required IReadOnlyList<string> Modes { get; init; }

    // The tokens of the Covers column, in order.
    public required IReadOnlyList<string> Covers { get; init; }

    // The planned (or existing) test method.
    public required string Method { get; init; }

    public required CatalogueSection Section { get; init; }

    // The title of the sub-folder heading ("Device Code").
    public required string SubFolder { get; init; }

    // The test class of the sub-folder heading ("DeviceCodeSignInTests").
    public required string ClassName { get; init; }

    public required int Line { get; init; }

    public long? TestinyId => long.TryParse(Testiny, NumberStyles.None, CultureInfo.InvariantCulture, out var id) ? id : null;

    // The full name of the test class: _42.Platform.Cli.AutomationTests.Tests.<Folder>.<Class>.
    public string ClassFullName => $"{CatalogueTests.TestsNamespace}.{Section.FolderName}.{ClassName}";

    // "Tests/SignIn/DeviceCodeSignInTests.DeviceCode_SignIn_ShowsAccount".
    public string Test => $"{Section.Folder}/{ClassName}.{Method}";

    // The features of the X:<Feature> modes.
    public IReadOnlyList<string> Features => [.. Modes.Where(mode => mode.StartsWith("X:", StringComparison.Ordinal)).Select(mode => mode[2..].Trim())];

    public bool HasMode(string mode) => Modes.Contains(mode, StringComparer.Ordinal);

    public override string ToString()
    {
        return $"{Reference} ({Testiny})";
    }
}
