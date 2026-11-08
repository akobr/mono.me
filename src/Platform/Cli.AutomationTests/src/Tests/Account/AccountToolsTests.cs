using System.Security.Cryptography.X509Certificates;
using System.Text.RegularExpressions;
using _42.Platform.Cli.AutomationTests.Data;
using _42.Platform.Cli.AutomationTests.Running;
using _42.Platform.Cli.AutomationTests.Running.Terminal;
using _42.Testing.UI.Automation;
using _42.Testing.UI.Automation.Runtime.TUnit;

namespace _42.Platform.Cli.AutomationTests.Tests.Account;

// Certificate Authority and Assistance: sform account ca, sform assist key, and the commands not available yet.
[Area(Area.Account)]
public sealed partial class AccountToolsTests : CliTest
{
    private static readonly TimeSpan PromptTimeout = TimeSpan.FromSeconds(30);

    // ACC-TLS-01: account ca prints the CA certificate as PEM without a sign-in, -o writes it to a file, and the context
    // options change nothing.
    // In an empty home (no sign-in): sform account ca prints a PEM certificate; sform account ca -o ca.pem and sform account
    // ca --output ca2.pem -p <org>.<project> -v <view> say "CA certificate written to ca.pem" (ca2.pem) and write it; sform
    // account ca --projectKey <org>.<generated> --view <generated> prints it again. Each ends with 0, and each is the same
    // certificate, an X.509 certificate with the CA flag: the certificate authority belongs to no project.
    [Test]
    [TestCase(312)]
    [UserRole(Role.Any)]
    [FreshSignIn]
    [Covers("account ca", "-o", "--output", "-p", "--projectKey", "-v", "--view")]
    public async Task Ca_Download_WritesPem()
    {
        var printed = (await Sform("account", "ca")).ShouldSucceed();

        (await Sform("account", "ca", "-o", "ca.pem"))
            .ShouldSucceed()
            .ShouldContain("CA certificate written to ca.pem");

        (await Sform("account", "ca", "--output", "ca2.pem", "-p", Target.ProjectKey, "-v", CliData.ViewName()))
            .ShouldSucceed()
            .ShouldContain("CA certificate written to ca2.pem");

        var elsewhere = (await Sform("account", "ca", "--projectKey", $"{Target.Organization}.{CliData.ProjectName()}", "--view", CliData.ViewName()))
            .ShouldSucceed();

        var expected = await ThumbprintOfCaAsync(PemOf(printed), "the PEM of account ca");
        await Assert.That(await ThumbprintOfCaAsync(await ReadFileAsync("ca.pem"), "ca.pem")).IsEqualTo(expected);
        await Assert.That(await ThumbprintOfCaAsync(await ReadFileAsync("ca2.pem"), "ca2.pem")).IsEqualTo(expected);
        await Assert.That(await ThumbprintOfCaAsync(PemOf(elsewhere), "the PEM of account ca --projectKey … --view …")).IsEqualTo(expected);
        await Assert.That(Home.HasSession).IsFalse();
    }

    // ACC-TLS-02: assist key builds a valid annotation key from the answers.
    // In a terminal, sform assist key asks for the type (Unit picked) and the names of the responsibility and the job
    // (my-app, nightly) and prints "Your annotation key should be unt.my-app.nightly". sform assistance key with
    // Responsibility (the preselected type) and a generated name prints "Your annotation key should be rst.<name>", which
    // sform story set accepts (run by an Editor in the target project).
    [Test]
    [TestCase(314)]
    [UserRole(Role.Any)]
    [Terminal]
    [Covers("assist key")]
    [Covers("assistance key")]
    public async Task AssistKey_Answers_PrintsKey()
    {
        await using (var unit = await StartSformInTerminal("assist", "key"))
        {
            await unit.ExpectAsync("What type of annotation you need");
            await unit.SendKeyAsync(TerminalKey.Down);
            await unit.SendKeyAsync(TerminalKey.Enter);
            await unit.ExpectAsync("Name of the responsibility");
            await unit.SendLineAsync("my-app");
            await unit.ExpectAsync("Name of the job");
            await unit.SendLineAsync("nightly");
            await unit.ExpectAsync("Your annotation key should be unt.my-app.nightly");
            (await unit.WaitForExitAsync()).ShouldSucceed();
        }

        var name = CliData.AnnotationPart();
        var key = $"{CliData.Responsibility}.{name}";
        await using (var responsibility = await StartSformInTerminal("assistance", "key"))
        {
            await responsibility.ExpectAsync("What type of annotation you need");
            await responsibility.SendKeyAsync(TerminalKey.Enter);
            await responsibility.ExpectAsync("Name of the responsibility");
            await responsibility.SendLineAsync(name);
            await responsibility.ExpectAsync($"Your annotation key should be {key}");
            (await responsibility.WaitForExitAsync()).ShouldSucceed();
        }

        var editor = await ArrangeAsEditorAsync();
        var set = (await editor.Sform(InProject("story", "set", key))).ShouldSucceed();
        await Assert.That(set.JsonNode()["AnnotationKey"]!.GetValue<string>()).IsEqualTo(key);
    }

    // ACC-TLS-03: assist and assist key on redirected input end with exit 43 instead of waiting.
    // With the input redirected, sform assist key ends with 43 within the timeout, stderr saying "! sform cannot ask for
    // the parts of the annotation key: the input is redirected." and "Run sform assist key in a terminal."; sform assist ends
    // with 43, stderr saying "! sform cannot ask for the assist command to run: the input is redirected." and "Name it: sform
    // assist key."
    [Test]
    [TestCase(317)]
    [UserRole(Role.Any)]
    [Covers("assist key")]
    [Covers("assist")]
    public async Task Assist_Redirected_FailsFast()
    {
        (await Sform(new SformRunOptions { Timeout = PromptTimeout }, "assist", "key"))
            .ShouldExitWith(CliExit.InteractionNeeded)
            .ShouldContain("! sform cannot ask for the parts of the annotation key: the input is redirected.", SformStream.StdErr)
            .ShouldContain("Run sform assist key in a terminal.", SformStream.StdErr)
            .ShouldNotContain("Your annotation key should be");

        (await Sform(new SformRunOptions { Timeout = PromptTimeout }, "assist"))
            .ShouldExitWith(CliExit.InteractionNeeded)
            .ShouldContain("! sform cannot ask for the assist command to run: the input is redirected.", SformStream.StdErr)
            .ShouldContain("Name it: sform assist key.", SformStream.StdErr);
    }

    // ACC-TLS-04: supervisor and scheduler (and supervise, schedule) print that they are not available yet.
    // sform supervisor and sform supervise print "Under the development, will be implemented in version 1.0", sform
    // scheduler and sform schedule "Under the development, will be implemented in version 1.1"; each ends with 0.
    [Test]
    [TestCase(319)]
    [UserRole(Role.Any)]
    [Covers("supervisor")]
    [Covers("supervise")]
    [Covers("scheduler")]
    [Covers("schedule")]
    public async Task Placeholders_PrintNotAvailable()
    {
        foreach (var (command, version) in new[] { ("supervisor", "1.0"), ("supervise", "1.0"), ("scheduler", "1.1"), ("schedule", "1.1") })
        {
            (await Sform(command))
                .ShouldSucceed()
                .ShouldContain($"Under the development, will be implemented in version {version}");
        }
    }

    // The first PEM certificate block of the standard output.
    private static string PemOf(SformResult result)
    {
        var match = PemBlock().Match(result.PlainStdOut);
        return match.Success ? match.Value : throw result.Fail("printed no PEM certificate (-----BEGIN CERTIFICATE----- … -----END CERTIFICATE-----)");
    }

    // The thumbprint of the PEM's first certificate, which must be a certificate authority (basic constraints with the CA
    // flag).
    private static async Task<string> ThumbprintOfCaAsync(string pem, string source)
    {
        await Assert.That(pem).Contains("-----BEGIN CERTIFICATE-----");
        using var certificate = X509Certificate2.CreateFromPem(pem);
        var constraints = certificate.Extensions.OfType<X509BasicConstraintsExtension>().FirstOrDefault();
        if (constraints is not { CertificateAuthority: true })
        {
            Assert.Fail($"The certificate of {source} ({certificate.Subject}) is not a certificate authority: it has no basic constraints with the CA flag.");
        }

        return certificate.Thumbprint;
    }

    [GeneratedRegex(@"-----BEGIN CERTIFICATE-----[A-Za-z0-9+/=\s]+?-----END CERTIFICATE-----", RegexOptions.CultureInvariant)]
    private static partial Regex PemBlock();
}
