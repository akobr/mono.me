using _42.Platform.Cli.AutomationTests.Running;

namespace _42.Platform.Cli.AutomationTests.Tests.Machines;

// A shared certificate a test issued (MachineAccessTest.IssueSharedCertificateAsync), read from what sform account
// shared-cert issue printed: "Shared certificate issued: <label>", "  Thumbprint: <thumbprint>", "  Valid: <from> to
// <to>", "Certificate written to <path>" and "Certificate password: <password>" (a secret of the test already).
public sealed record IssuedCertificate
{
    // The label as the server stored it (a duplicate gets a timestamp appended).
    public required string Label { get; init; }

    public required string Thumbprint { get; init; }

    // The full path of the .pfx sform wrote.
    public required string Path { get; init; }

    public string? Password { get; init; }

    // The expiry as "Valid: … to <yyyy-MM-dd>" printed it, the form of the Expires column of account shared-cert.
    public string? ValidTo { get; init; }

    // The project it belongs to.
    public required string ProjectKey { get; init; }

    public required SformResult Result { get; init; }
}
