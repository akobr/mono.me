using System.Buffers.Binary;
using System.Diagnostics;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using _42.Platform.Storyteller.Transferring;
using _42.Platform.Storyteller.Transferring.Git;
using static _42.Platform.Storyteller.Backend.Core.UnitTests.Transferring.TransferTestData;

namespace _42.Platform.Storyteller.Backend.Core.UnitTests.Transferring;

public class GitRepositoryTests
{
    [Fact]
    public void ObjectIds_AreTheIdsOfGit()
    {
        GitObjectId.ToHex(GitObjectId.Compute(GitObjectType.Blob, "hello\n"u8)).ShouldBe("ce013625030ba8dba906f756967f9e9ca394464a");
        GitObjectId.ToHex(GitObjectId.Compute(GitObjectType.Tree, [])).ShouldBe("4b825dc642cb6eb9a060e54bf8d69288fbee4904");
    }

    [Fact]
    public void Crc32_IsTheCrcOfZlib()
    {
        Crc32.Compute("123456789"u8).ShouldBe(0xCBF43926u);
        Crc32.Compute([]).ShouldBe(0u);
    }

    [Theory]
    [InlineData("", "")]
    [InlineData("", "new")]
    [InlineData("old", "")]
    [InlineData("same", "same")]
    [InlineData("{\"a\":1,\"b\":2}", "{\"a\":1,\"b\":3}")]
    [InlineData("prefix-middle-suffix", "prefix-suffix")]
    [InlineData("prefix-suffix", "prefix-a-much-longer-middle-suffix")]
    [InlineData("aaaa", "aaaaaa")]
    [InlineData("abc", "xyz")]
    public void Delta_TurnsTheBaseIntoTheTarget(string source, string target)
    {
        var sourceBytes = Encoding.UTF8.GetBytes(source);
        var targetBytes = Encoding.UTF8.GetBytes(target);

        GitTestReader.ApplyDelta(sourceBytes, GitDelta.Create(sourceBytes, targetBytes)).ShouldBe(targetBytes);
    }

    [Fact]
    public void Delta_CopiesMoreThan64KiB()
    {
        var source = RandomNumberGenerator.GetBytes(200_000);
        var target = source.Take(150_000).Concat("changed"u8.ToArray()).Concat(source.Skip(150_010)).ToArray();

        var delta = GitDelta.Create(source, target);

        GitTestReader.ApplyDelta(source, delta).ShouldBe(target);
        delta.Length.ShouldBeLessThan(64);
    }

    [Fact]
    public void Git_CommitsEveryVersionInTimeOrder()
    {
        var zip = Write(
            ViewArchiveFormat.Git,
            History("rst.b", true, (1, "account: user_02", 2, """{"b":1}"""), (2, "account: user_02", 10, """{"b":2}""")),
            History("rst.a", true, (1, "account: user_01", 0, """{"a":1}"""), (2, "application: machine-1", 5, """{"a":2}""")));

        var history = new GitTestReader(zip).GetHistory();

        history.Select(commit => commit.Message).ShouldBe(
        [
            "Add rst.a (version 1)\n",
            "Add rst.b (version 1)\n",
            "Update rst.a to version 2\n",
            "Update rst.b to version 2\n",
        ]);

        history[0].Author.ShouldBe($"account: user_01 <account.user_01@2s.platform> {Start.ToUnixTimeSeconds()} +0000");
        history[2].Author.ShouldBe($"application: machine-1 <application.machine-1@2s.platform> {Start.AddMinutes(5).ToUnixTimeSeconds()} +0000");
        history[2].Committer.ShouldBe(history[2].Author);

        history[0].Files.Keys.ShouldBe(["rst.a.conf.json"]);
        history[1].Files.Keys.ShouldBe(["rst.a.conf.json", "rst.b.conf.json"]);
        history[2].Files["rst.a.conf.json"].ShouldBe("{\n  \"a\": 2\n}\n");
        history[2].Files["rst.b.conf.json"].ShouldBe("{\n  \"b\": 1\n}\n");
        history[3].Files["rst.b.conf.json"].ShouldBe("{\n  \"b\": 2\n}\n");
    }

    [Fact]
    public void Git_KeepsTheVersionsOfAKeyInOrderWhenTimesAreEqual()
    {
        var zip = Write(
            ViewArchiveFormat.Git,
            History("rst.a", true, (1, "a", 0, """{"a":1}"""), (2, "a", 0, """{"a":2}"""), (3, "a", 0, """{"a":3}""")));

        new GitTestReader(zip).GetHistory().Select(commit => commit.Message.Trim()).ShouldBe(
            ["Add rst.a (version 1)", "Update rst.a to version 2", "Update rst.a to version 3"]);
    }

    [Fact]
    public void Git_RemovesClearedConfigurationsInTheLastCommit()
    {
        var zip = Write(
            ViewArchiveFormat.Git,
            History("rst.cleared", false, (1, "a", 0, """{"c":1}""")),
            History("rst.kept", true, (1, "a", 1, """{"k":1}""")));

        var reader = new GitTestReader(zip);
        var history = reader.GetHistory();

        history.Count.ShouldBe(3);
        history[^1].Message.ShouldBe("Remove cleared configurations\n\nrst.cleared\n");
        history[^1].Author.ShouldStartWith("2S Platform <system@2s.platform> ");
        history[^1].Files.Keys.ShouldBe(["rst.kept.conf.json"]);
        reader.Files.Keys.Where(name => !name.StartsWith(".git/", StringComparison.Ordinal)).ShouldBe(["rst.kept.conf.json"]);
    }

    [Fact]
    public void Git_ChecksOutTheLastCommitWithItsIndex()
    {
        var zip = Write(
            ViewArchiveFormat.Git,
            History("usg.shop.payments", true, (1, "a", 0, """{"u":1}""")),
            History("rst.a:b", true, (1, "a", 1, """{"a":1}"""), (2, "a", 2, """{"a":2}""")));

        var reader = new GitTestReader(zip);
        var last = reader.GetHistory()[^1];
        var workingTree = reader.Files
            .Where(file => !file.Key.StartsWith(".git/", StringComparison.Ordinal))
            .ToDictionary(file => file.Key, file => Encoding.UTF8.GetString(file.Value));

        workingTree.ShouldBe(last.Files, ignoreOrder: true);
        workingTree.Keys.ShouldContain("rst.a%3Ab.conf.json");

        reader.Head.ShouldBe("ref: refs/heads/main\n");
        reader.Files.ShouldContainKey(".git/config");
        reader.Files.ShouldContainKey(".git/refs/tags/");

        var index = reader.Files[".git/index"];
        Encoding.ASCII.GetString(index, 0, 4).ShouldBe("DIRC");
        BinaryPrimitives.ReadUInt32BigEndian(index.AsSpan(4)).ShouldBe(2u);
        BinaryPrimitives.ReadUInt32BigEndian(index.AsSpan(8)).ShouldBe(2u);
        SHA1.HashData(index.AsSpan(0, index.Length - 20)).ShouldBe(index.AsSpan(index.Length - 20).ToArray());

        var position = 12;
        var names = new List<string>();

        for (var entry = 0; entry < 2; entry++)
        {
            BinaryPrimitives.ReadUInt32BigEndian(index.AsSpan(position + 24)).ShouldBe(0x81A4u);
            var size = BinaryPrimitives.ReadUInt32BigEndian(index.AsSpan(position + 36));
            var id = Convert.ToHexStringLower(index.AsSpan(position + 40, 20));
            var nameLength = BinaryPrimitives.ReadUInt16BigEndian(index.AsSpan(position + 60)) & 0xFFF;
            var name = Encoding.UTF8.GetString(index, position + 62, nameLength);

            var blob = reader.GetObject(id);
            blob.Type.ShouldBe("blob");
            Encoding.UTF8.GetString(blob.Content).ShouldBe(workingTree[name]);
            size.ShouldBe((uint)blob.Content.Length);

            names.Add(name);
            position += 62 + nameLength + (8 - ((62 + nameLength) % 8));
        }

        position.ShouldBe(index.Length - 20);
        names.ShouldBe(["rst.a%3Ab.conf.json", "usg.shop.payments.conf.json"]);
    }

    [Fact]
    public void Git_EmptyViewIsAnUnbornBranch()
    {
        var reader = new GitTestReader(Write(ViewArchiveFormat.Git));

        reader.Head.ShouldBe("ref: refs/heads/main\n");
        reader.Branch.ShouldBeNull();
        reader.ObjectCount.ShouldBe(0);
        reader.Files.Keys.ShouldNotContain(".git/index");
        reader.Files.Keys.ShouldContain(".git/objects/");
    }

    [Fact]
    public void Git_StoresConsecutiveVersionsAsDeltas()
    {
        var properties = string.Join(",", Enumerable.Range(0, 200).Select(index => $"\"p{index}\":\"value number {index}\""));
        var versions = Enumerable.Range(1, 120)
            .Select(version => ((ulong)version, "a", version, $"{{{properties},\"version\":{version}}}"))
            .ToArray();

        var histories = Enumerable.Range(0, 30)
            .Select(index => History($"rst.k{index:D2}", true, (1, "a", index, $"{{{properties},\"key\":{index}}}")))
            .Append(History("rst.versioned", true, versions))
            .ToArray();

        var zip = Write(ViewArchiveFormat.Git, histories);
        var reader = new GitTestReader(zip);

        reader.GetHistory().Count.ShouldBe(150);
        reader.DeltaCount.ShouldBeGreaterThan(200);

        var pack = reader.Files.Single(file => file.Key.EndsWith(".pack", StringComparison.Ordinal)).Value;
        var rawDocuments = histories.Sum(history => history.Versions.Sum(version => ViewDocumentJson.Serialize(version.Content).Length));
        pack.Length.ShouldBeLessThan(rawDocuments / 5);
    }

    [Fact]
    public void Git_PackIsStoredWithoutRecompression()
    {
        var zip = Write(ViewArchiveFormat.Git, History("rst.a", true, (1, "a", 0, """{"a":1}""")));

        using var archive = new ZipArchive(new MemoryStream(zip), ZipArchiveMode.Read);
        var pack = archive.Entries.Single(entry => entry.FullName.EndsWith(".pack", StringComparison.Ordinal));
        pack.CompressedLength.ShouldBe(pack.Length);
    }

    [Fact]
    public void Signature_DerivesAnEmailFromTheAuthor()
    {
        var at = new DateTimeOffset(2026, 10, 14, 1, 30, 12, TimeSpan.Zero);

        GitSignature.FromAuthor("account: user_01", at).Email.ShouldBe("account.user_01@2s.platform");
        GitSignature.FromAuthor("system", at).Email.ShouldBe("system@2s.platform");
        GitSignature.FromAuthor("account: Ada <ada@x>", at).Format().ShouldBe($"account: Ada ada@x <account.Ada-ada-x@2s.platform> {at.ToUnixTimeSeconds()} +0000");
        GitSignature.FromAuthor(null, at).Format().ShouldBe($"unknown <unknown@2s.platform> {at.ToUnixTimeSeconds()} +0000");
    }

    // The real git, when it is installed, reads the exported repository like any clone.
    [Fact]
    public void GitCli_AcceptsTheRepository()
    {
        if (!IsGitInstalled())
        {
            return;
        }

        var zip = Write(
            ViewArchiveFormat.Git,
            History("rst.payments", true, (1, "account: user_01", 0, """{"retries":1}"""), (2, "account: user_02", 5, """{"retries":2}""")),
            History("sbt.shop", true, (1, "application: machine-1", 3, """{"theme":"dark"}""")),
            History("rst.cleared", false, (1, "account: user_01", 1, """{"old":true}""")));

        var directory = Directory.CreateTempSubdirectory("configurations-git-");

        try
        {
            ZipFile.ExtractToDirectory(new MemoryStream(zip), directory.FullName);

            RunGit(directory.FullName, "fsck --full --strict --no-dangling").ShouldBeEmpty();
            RunGit(directory.FullName, "status --porcelain").ShouldBeEmpty();
            RunGit(directory.FullName, "log --reverse --format=%an|%ae|%at|%s").ShouldBe(string.Join('\n',
            [
                $"account: user_01|account.user_01@2s.platform|{Start.ToUnixTimeSeconds()}|Add rst.payments (version 1)",
                $"account: user_01|account.user_01@2s.platform|{Start.AddMinutes(1).ToUnixTimeSeconds()}|Add rst.cleared (version 1)",
                $"application: machine-1|application.machine-1@2s.platform|{Start.AddMinutes(3).ToUnixTimeSeconds()}|Add sbt.shop (version 1)",
                $"account: user_02|account.user_02@2s.platform|{Start.AddMinutes(5).ToUnixTimeSeconds()}|Update rst.payments to version 2",
                $"2S Platform|system@2s.platform|{Start.AddDays(1).ToUnixTimeSeconds()}|Remove cleared configurations",
            ]));
            RunGit(directory.FullName, "show HEAD~4:rst.payments.conf.json").ShouldBe("{\n  \"retries\": 1\n}");
            RunGit(directory.FullName, "ls-files").ShouldBe("rst.payments.conf.json\nsbt.shop.conf.json");
        }
        finally
        {
            directory.Delete(recursive: true);
        }
    }

    private static bool IsGitInstalled()
    {
        try
        {
            RunGit(Path.GetTempPath(), "--version");
            return true;
        }
        catch (Exception exception) when (exception is System.ComponentModel.Win32Exception or InvalidOperationException)
        {
            return false;
        }
    }

    private static string RunGit(string directory, string arguments)
    {
        var start = new ProcessStartInfo("git", $"-c core.autocrlf=false -c safe.directory=* {arguments}")
        {
            WorkingDirectory = directory,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            StandardOutputEncoding = Encoding.UTF8,
        };

        using var process = Process.Start(start) ?? throw new InvalidOperationException("git did not start.");
        var output = process.StandardOutput.ReadToEndAsync();
        var error = process.StandardError.ReadToEndAsync();
        process.WaitForExit();

        if (process.ExitCode != 0)
        {
            throw new InvalidOperationException($"git {arguments} failed ({process.ExitCode}): {error.Result}");
        }

        return output.Result.Replace("\r\n", "\n").TrimEnd('\n');
    }
}
