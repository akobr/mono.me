using _42.Platform.Cli.AutomationTests.Access;
using _42.Platform.Cli.AutomationTests.Running;
using _42.Platform.Cli.AutomationTests.Running.Processes;

namespace _42.Platform.Cli.AutomationTests.SelfTests;

// What the tests read from sform's output, without a process: the escape sequences removed, the JSON of WriteJson behind
// its gutter, the device code of SFORM_DEVICE_CODE_OUTPUT=json and of the text prompt, the credentials sform prints once,
// the command lines of a pseudo console and of util-linux script, a machine's id in its API key, ShouldNotMatch and the
// rows of a versions table (VersionRows).
[Category(CliCategories.SelfTest)]
public sealed class OutputSelfTests
{
    [Test]
    public async Task Plain_RemovesColoursTitlesAndCursorMoves()
    {
        const string text = "\u001b]0;sform\u0007\u001b[1;31mError\u001b[0m:\u001b[3Cnot\u001b[2Xfound\r\n\u001b[5;1Hnext\rredrawn\u001b(B\u001b[?25l";

        var plain = AnsiText.Plain(text);

        await Assert.That(plain).IsEqualTo("Error:   not  found\n\nnext\nredrawn");
    }

    [Test]
    public async Task CompletePrefix_CutsAnUnfinishedEscape()
    {
        await Assert.That(AnsiText.CompletePrefixLength("abc\u001b[1;3")).IsEqualTo(3);
        await Assert.That(AnsiText.CompletePrefixLength("abc\u001b[1;31m")).IsEqualTo(10);
        await Assert.That(AnsiText.CompletePrefixLength("abc\u001b]0;title")).IsEqualTo(3);
        await Assert.That(AnsiText.CompletePrefixLength("abc")).IsEqualTo(3);
    }

    // WriteJson: an empty line, then " n │ " and the coloured JSON line; the gutter goes, the colours go.
    [Test]
    public async Task Json_IsReadFromTheNumberedOutput()
    {
        const string stdout = "\n\u001b[90m1\u001b[0m \u001b[2m│\u001b[0m \u001b[38;2;1;2;3m{\u001b[0m\n"
            + "\u001b[90m2\u001b[0m \u001b[2m│\u001b[0m   \u001b[34m\"AnnotationKey\"\u001b[0m: \u001b[33m\"rst.cli-1\"\u001b[0m,\n"
            + "\u001b[90m3\u001b[0m \u001b[2m│\u001b[0m   \"Labels\": [ \"a\" ]\n"
            + "\u001b[90m4\u001b[0m \u001b[2m│\u001b[0m }\n"
            + "Make sure to copy the access key.\n";
        var result = Result(stdout);

        var json = result.JsonNode();

        await Assert.That(json["AnnotationKey"]!.GetValue<string>()).IsEqualTo("rst.cli-1");
        await Assert.That(result.Json<Annotation>().AnnotationKey).IsEqualTo("rst.cli-1");
    }

    [Test]
    public async Task Json_IsTheWholeOutputOfContentOnly()
    {
        var result = Result("{\n  \"a\": 1,\n  \"b\": { \"c\": true }\n}\n");

        var json = result.JsonNode();

        await Assert.That(json["b"]!["c"]!.GetValue<bool>()).IsTrue();
    }

    [Test]
    public async Task Json_IsTheFirstBalancedDocument()
    {
        var result = Result("Version 3 of the pair:\n{ \"Key\": \"x\", \"Value\": \"{not json}\" } and more\n");

        await Assert.That(result.JsonNode()["Value"]!.GetValue<string>()).IsEqualTo("{not json}");
    }

    [Test]
    public async Task NoJson_FailsWithTheOutput()
    {
        var result = Result("Nothing here.\n");

        SformAssertionException? failure = null;
        try
        {
            result.JsonNode();
        }
        catch (SformAssertionException exception)
        {
            failure = exception;
        }

        await Assert.That(failure).IsNotNull();
        await Assert.That(failure!.Message).Contains("printed no JSON document");
        await Assert.That(failure.Message).Contains("Nothing here.");
    }

    [Test]
    public async Task Assertions_MaskTheSecretsInTheirMessages()
    {
        var result = Result("the key is s3cr3t-value\n", redact: text => (text ?? string.Empty).Replace("s3cr3t-value", "***", StringComparison.Ordinal));

        SformAssertionException? failure = null;
        try
        {
            result.ShouldExitWith(CliExit.Unauthorized);
        }
        catch (SformAssertionException exception)
        {
            failure = exception;
        }

        await Assert.That(failure!.Message).Contains("the key is ***");
        await Assert.That(failure.Message).DoesNotContain("s3cr3t-value");
        await Assert.That(failure.Message).Contains("expected 44 (Unauthorized)");
    }

    // The contract of SFORM_DEVICE_CODE_OUTPUT=json (DeviceCodePrompt.ToJsonLine of the CLI).
    [Test]
    public async Task DeviceCode_JsonLineIsParsed()
    {
        const string line = "{\"userCode\":\"ABCD-EFGH\",\"verificationUri\":\"https://auth.example.com/device\",\"verificationUriComplete\":\"https://auth.example.com/device?user_code=ABCD-EFGH&x=1+2\",\"expiresIn\":300}";

        var prompt = DeviceCodePrompt.Parse(line);

        await Assert.That(prompt.UserCode).IsEqualTo("ABCD-EFGH");
        await Assert.That(prompt.VerificationUri.AbsoluteUri).IsEqualTo("https://auth.example.com/device");
        await Assert.That(prompt.Address.Query).Contains("user_code=ABCD-EFGH");
        await Assert.That(prompt.ExpiresIn).IsEqualTo(300);
        await Assert.That(DeviceCodePrompt.IsPrompt("# Sign in")).IsFalse();
        await Assert.That(DeviceCodePrompt.IsPrompt("{\"userCode\":\"X\",\"verificationUri\":null}")).IsFalse();
    }

    [Test]
    public async Task DeviceCode_WithoutCompleteUriUsesTheVerificationUri()
    {
        var prompt = DeviceCodePrompt.Parse("{\"userCode\":\"Q1\",\"verificationUri\":\"https://login.microsoftonline.com/device\",\"verificationUriComplete\":null,\"expiresIn\":0}");

        await Assert.That(prompt.VerificationUriComplete).IsNull();
        await Assert.That(prompt.Address.Host).IsEqualTo("login.microsoftonline.com");
    }

    [Test]
    public async Task DeviceCode_TextPromptIsParsed()
    {
        const string output = "# Sign in\nTo sign in, open https://auth.example.com/device and enter the code WXYZ-1234.\nOr open https://auth.example.com/device?user_code=WXYZ-1234 with the code filled in.\nThe code expires in 5 minute(s).\n";

        await Assert.That(DeviceCodePrompt.TryParseText(output, out var prompt)).IsTrue();
        await Assert.That(prompt.UserCode).IsEqualTo("WXYZ-1234");
        await Assert.That(prompt.VerificationUriComplete!.Query).IsEqualTo("?user_code=WXYZ-1234");
        await Assert.That(prompt.ExpiresIn).IsEqualTo(300);
    }

    [Test]
    public async Task Secrets_PrintedOnceAreFound()
    {
        const string output = "1 │ {\n2 │   \"Id\": \"m1\",\n3 │   \"AccessKey\": \"2s.b3JnMTpwcm9qMTptMQ.SECRETpart0123456789\",\n4 │   \"CertificatePassword\": \"[see below]\"\n5 │ }\n"
            + "Client secret: cs-0123456789abcdef\nCertificate password: pfx-pass-42\n";

        var found = SecretScanner.Find(output);

        await Assert.That(found).Contains("2s.b3JnMTpwcm9qMTptMQ.SECRETpart0123456789");
        await Assert.That(found).Contains("cs-0123456789abcdef");
        await Assert.That(found).Contains("pfx-pass-42");
        await Assert.That(found).DoesNotContain("[see below]");
    }

    [Test]
    public async Task ApiKey_NamesItsMachineAndProject()
    {
        await Assert.That(MachineCredentials.MachineIdOf("2s.b3JnMTpwcm9qMTptMQ.secret")).IsEqualTo("m1");
        await Assert.That(MachineCredentials.ProjectKeyOf("ApiKey 2s.b3JnMTpwcm9qMTptMQ.secret")).IsEqualTo("org1.proj1");
        await Assert.That(MachineCredentials.MachineIdOf("not-structured")).IsNull();
    }

    [Test]
    public async Task CommandLines_QuoteForWindowsAndTheShell()
    {
        await Assert.That(CommandLines.Windows(["C:\\Program Files\\dotnet\\dotnet.exe", "a b", "quote\"d", "trail\\", string.Empty]))
            .IsEqualTo("\"C:\\Program Files\\dotnet\\dotnet.exe\" \"a b\" \"quote\\\"d\" trail\\ \"\"");
        await Assert.That(CommandLines.PosixShell(["/usr/bin/dotnet", "it's", "$HOME"])).IsEqualTo("'/usr/bin/dotnet' 'it'\\''s' '$HOME'");
        await Assert.That(CommandLines.Display(["story", "set", "rst.a b"])).IsEqualTo("story set 'rst.a b'");
    }

    // WriteJson renders at 80 columns whatever COLUMNS says: these are the lines Spectre.Console.Json 0.57.2 (the CLI's)
    // renders for this model, captured as WriteJson captures them. A value that does not fit moves to the next line, a
    // token longer than 80 is folded into lines of 80, a string with spaces wraps at a space and loses it.
    [Test]
    public async Task WrappedJson_FoldsAreJoined()
    {
        var apiKey = "2s." + new string('A', 76) + "." + new string('s', 43);
        var unitKey = "unt." + new string('k', 39) + "." + new string('j', 39);
        var result = Result(WriteJsonOutput(apiKey), redact: text => (text ?? string.Empty).Replace(apiKey, "***", StringComparison.Ordinal));

        var json = result.JsonNode();

        await Assert.That(json["AccessKey"]!.GetValue<string>()).IsEqualTo(apiKey);
        await Assert.That(json["AnnotationKey"]!.GetValue<string>()).IsEqualTo(unitKey);
        await Assert.That(json["Description"]!.GetValue<string>()).IsEqualTo("a value with spaces that is rather long and keeps going for a while so that it wraps somewhere   in the middle");
        await Assert.That(json["Escaped"]!.GetValue<string>()).IsEqualTo("quote \" and backslash \\ and more text to push it over the edge of eighty columns okay");
        await Assert.That(json["Big"]!.GetValue<string>()).IsEqualTo(new string('z', 200));
        await Assert.That(json["Nested"]!["Deep"]!["Deeper"]!["Key"]!.GetValue<string>()).IsEqualTo("x" + new string('y', 70));
        result.ShouldContain(unitKey).ShouldContain(apiKey);

        // The key is found whole, so the attachment and the messages mask it whole: no half of it is left.
        await Assert.That(SecretScanner.Find(result.PlainStdOut)).Contains(apiKey);
        var described = result.Describe();
        await Assert.That(described).DoesNotContain(new string('s', 43));
        await Assert.That(described).DoesNotContain(new string('A', 76));
        await Assert.That(AnsiText.Plain(result.StdOut)).Contains(new string('A', 76) + "\n");
    }

    // Numbered text that is no JSON (a --format payload of WriteNumberedText) and a block that is JSON as printed stay.
    [Test]
    public async Task WrappedJson_LeavesOtherTextAlone()
    {
        const string flat = "\n1 │ a.b = \"open quote\n2 │ c = 1\n";
        const string json = "1 │ {\n2 │   \"a\": \"x\"\n3 │ }\n";

        await Assert.That(WrappedJson.Join(flat)).IsEqualTo(flat);
        await Assert.That(WrappedJson.Join(json)).IsEqualTo(json);
        await Assert.That(WrappedJson.Join("no lines")).IsEqualTo("no lines");
    }

    // On Linux and macOS an exit status is a byte: sform's -1 arrives as 255.
    [Test]
    public async Task ExitCode_255IsTheParseCodeWhereAStatusIsAByte()
    {
        await Assert.That(SformResult.ExitCodeOf(255, windows: false)).IsEqualTo(-1);
        await Assert.That(SformResult.ExitCodeOf(-1, windows: true)).IsEqualTo(-1);
        await Assert.That(SformResult.ExitCodeOf(255, windows: true)).IsEqualTo(255);
        await Assert.That(SformResult.ExitCodeOf(44, windows: false)).IsEqualTo(44);

        var result = new SformResult(["storytelr"], "sform storytelr", OperatingSystem.IsWindows() ? -1 : 255, string.Empty, string.Empty, [], TimeSpan.Zero, false, ".", ".");
        result.ShouldExitWith(CliExit.InputParsing);
        await Assert.That(result.Exit).IsEqualTo(CliExit.InputParsing);
    }

    // config -r --github-env/--mask prints ::add-mask:: for exactly the values sform treats as secret.
    [Test]
    public async Task Secrets_OfMaskCommandsAreFound()
    {
        var found = SecretScanner.Find("::add-mask::p%25ss%0D%0Aword-two\nOTHER=1\n::add-mask::kv-secret-value\n");

        await Assert.That(found).Contains("p%ss\r\nword-two");
        await Assert.That(found).Contains("word-two");
        await Assert.That(found).Contains("kv-secret-value");
        await Assert.That(SecretScanner.UnescapeWorkflowCommand("a%250Ab")).IsEqualTo("a%0Ab");
    }

    // A failed leak check must not print the secret it guards.
    [Test]
    public async Task Assertions_MaskWhatTheyLookFor()
    {
        const string secret = "kv-s3cr3t-value";
        var result = Result($"value: {secret}\n", redact: text => (text ?? string.Empty).Replace(secret, "***", StringComparison.Ordinal));

        SformAssertionException? failure = null;
        try
        {
            result.ShouldNotContain(secret);
        }
        catch (SformAssertionException exception)
        {
            failure = exception;
        }

        await Assert.That(failure).IsNotNull();
        await Assert.That(failure!.Message).DoesNotContain(secret);
        await Assert.That(failure.Message).Contains("wrote '***'");
    }

    // ShouldNotMatch passes when nothing of the stream matches and fails, with the pattern, when something does.
    [Test]
    public async Task ShouldNotMatch_FailsOnlyForAMatch()
    {
        var result = Result("Unrecognized command or argument 'storytelr'\n");
        var frame = new System.Text.RegularExpressions.Regex(@"^\s+at \S+\(", System.Text.RegularExpressions.RegexOptions.Multiline);

        await Assert.That(ReferenceEquals(result.ShouldNotMatch(frame), result)).IsTrue();
        await Assert.That(ReferenceEquals(result.ShouldNotMatch(frame, SformStream.StdErr), result)).IsTrue();

        SformAssertionException? failure = null;
        try
        {
            Result("Unhandled exception.\n   at Program.Main(String[] args)\n").ShouldNotMatch(frame);
        }
        catch (SformAssertionException exception)
        {
            failure = exception;
        }

        await Assert.That(failure).IsNotNull();
        await Assert.That(failure!.Message).Contains("which it must not");
    }

    // The table of WriteVersions after its header, as config, template and schema … versions print it: the version of each
    // row, the newest first; nothing for "… has no versions."; a failure without the header.
    [Test]
    public async Task VersionRows_ReadTheVersionsTable()
    {
        var table = Result(
            "# Versions of the configuration for 'rst.cli-1'\n\n"
            + " Version  Author            Created           Expires          \n"
            + " 3        CLI Owner1        2026-11-08 10:02  —                \n"
            + " 2        CLI Owner1        2026-11-08 10:01  2026-12-08 10:02 \n"
            + " 1        cli-test (owner)  2026-11-08 10:00  2026-12-08 10:01 \n");
        var empty = Result("# Versions of the schema for 'unt'\n\nThe schema has no versions.\n");

        await Assert.That(string.Join(",", table.VersionRows("Versions of the configuration for"))).IsEqualTo("3,2,1");
        await Assert.That(empty.VersionRows("Versions of the schema for")).IsEmpty();

        SformAssertionException? failure = null;
        try
        {
            table.VersionRows("Versions of the template for");
        }
        catch (SformAssertionException exception)
        {
            failure = exception;
        }

        await Assert.That(failure).IsNotNull();
        await Assert.That(failure!.Message).Contains("printed no \"Versions of the template for …\" header");
    }

    // A .NET child on Windows writes its redirected output in the OEM code page of its hidden console, and UTF-8 around a
    // formatted payload: valid UTF-8 is read as such, any other byte in the code page, also across chunks.
    [Test]
    public async Task ConsoleOutput_MixesUtf8AndTheOemCodePage()
    {
        var oem = System.Text.CodePagesEncodingProvider.Instance.GetEncoding(437)!;
        var decoder = new ConsoleOutputDecoder(oem);
        byte[] bytes = [0x31, 0x20, 0xB3, 0x20, 0x7B, 0x0A, .. System.Text.Encoding.UTF8.GetBytes("2 │ žluť ✓"), 0x0A];

        var whole = new ConsoleOutputDecoder(oem).Decode(bytes, flush: true);
        var split = string.Concat(Enumerable.Range(0, bytes.Length).Select(index => decoder.Decode(bytes.AsSpan(index, 1))).Append(decoder.Decode([], flush: true)));

        await Assert.That(whole).IsEqualTo("1 │ {\n2 │ žluť ✓\n");
        await Assert.That(split).IsEqualTo(whole);
    }

    // SformRunOptions.WithoutHome: no SFORM_HOME; off Windows HOME and the XDG folders point at the run's own folder.
    [Test]
    public async Task Environment_WithoutHome_LeavesSformHomeOut()
    {
        var machine = new System.Collections.Hashtable { ["PATH"] = "/usr/bin", ["HOME"] = "/home/runner", ["USERPROFILE"] = "C:\\Users\\runner" };
        IReadOnlyDictionary<string, string?>[] overrides = [new Dictionary<string, string?> { ["SFORM_TOKEN_STORE"] = "file" }];

        var linux = SformProcessEnvironment.Build("/tmp/run/home", overrides, machine, withoutHome: true, windows: false);
        var windows = SformProcessEnvironment.Build("C:\\run\\home", overrides, machine, withoutHome: true, windows: true);

        await Assert.That(linux.ContainsKey("SFORM_HOME")).IsFalse();
        await Assert.That(linux["HOME"]).IsEqualTo("/tmp/run/home");
        await Assert.That(linux.ContainsKey("XDG_CONFIG_HOME")).IsTrue();
        await Assert.That(linux["SFORM_TOKEN_STORE"]).IsEqualTo("file");
        await Assert.That(windows.ContainsKey("SFORM_HOME")).IsFalse();
        await Assert.That(windows["USERPROFILE"]).IsEqualTo("C:\\Users\\runner");
    }

    [Test]
    public async Task Account_IdIsRead()
    {
        var result = new SformResult(["account"], "sform account", 43, "# Owner One @ cli-test-owner-1@42for.software\nAccount ID: #a1b2c3\nYou have access to 2 access points.\n", string.Empty, [], TimeSpan.Zero, false, ".", ".");

        await Assert.That(AccountOutput.AccountIdOf(result)).IsEqualTo("a1b2c3");
    }

    [Test]
    public async Task MachineCredentials_ForFederatedAndFromVariables()
    {
        var federated = MachineCredentials.ForFederated("m-7", "/work/token.jwt", "https://api.example.com/api", "org1.proj1");
        var custom = MachineCredentials.FromVariables("custom", new Dictionary<string, string?> { [MachineCredentials.ApiKeyVariable] = "2s.b3JnMTpwcm9qMTptMQ.secret-part", ["STORYTELLER_SUBJECT_TOKEN_TYPE"] = "jwt" }, ["2s.b3JnMTpwcm9qMTptMQ.secret-part"]);

        await Assert.That(federated.Variables[MachineCredentials.TokenFile]).IsEqualTo("/work/token.jwt");
        await Assert.That(federated.Variables.ContainsKey(MachineCredentials.ClientSecret)).IsFalse();
        await Assert.That(federated.Secrets).IsEmpty();
        await Assert.That(custom.MachineId).IsEqualTo("m1");
        await Assert.That(custom.Secrets).Contains("2s.b3JnMTpwcm9qMTptMQ.secret-part");
    }

    // What WriteJson prints for the model of WrappedJson_FoldsAreJoined: the rendered lines, each behind " n │ ".
    private static string WriteJsonOutput(string apiKey)
    {
        string[] rendered =
        [
            "{",
            "   \"Id\": \"m-12345678\",",
            "   \"AccessKey\": ",
            "\"" + apiKey[..79],
            apiKey[79..] + "\",",
            "   \"AnnotationKey\": ",
            "\"unt." + new string('k', 39) + "." + new string('j', 35),
            "jjjj\",",
            "   \"Description\": \"a value with spaces that is rather long and keeps going for a",
            "while so that it wraps somewhere   in the middle\",",
            "   \"Scopes\": [",
            "      \"AnnotationRead\",",
            "      \"ConfigurationRead\"",
            "   ],",
            "   \"Nested\": {",
            "      \"Deep\": {",
            "         \"Deeper\": {",
            "            \"Key\": ",
            "\"x" + new string('y', 70) + "\"",
            "         }",
            "      }",
            "   },",
            "   \"Escaped\": \"quote \\\" and backslash \\\\ and more text to push it over the edge ",
            "of eighty columns okay\",",
            "   \"Big\": ",
            "\"" + new string('z', 79),
            new string('z', 80),
            new string('z', 41) + "\"",
            "}",
        ];

        var width = rendered.Length.ToString(System.Globalization.CultureInfo.InvariantCulture).Length;
        return "\n" + string.Concat(rendered.Select((line, index) => $"\u001b[90m{(index + 1).ToString(System.Globalization.CultureInfo.InvariantCulture).PadLeft(width)}\u001b[0m \u001b[2m│\u001b[0m {line}\n"))
            + "\n! Make sure to copy the access key (secret), it is not stored anywhere.\n";
    }

    private static SformResult Result(string stdout, Func<string?, string>? redact = null)
    {
        return new SformResult(["story", "get", "rst.cli-1"], "sform story get rst.cli-1", 0, stdout, string.Empty, [], TimeSpan.FromSeconds(1), false, ".", ".", redact);
    }

    private sealed record Annotation(string AnnotationKey, IReadOnlyList<string> Labels);
}
