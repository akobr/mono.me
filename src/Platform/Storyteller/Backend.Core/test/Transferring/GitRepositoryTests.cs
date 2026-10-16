using System.Buffers.Binary;
using System.Diagnostics;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using _42.Platform.Storyteller.Transferring;
using _42.Platform.Storyteller.Transferring.Git;
using Newtonsoft.Json.Linq;
using static _42.Platform.Storyteller.Backend.Core.UnitTests.Transferring.TransferTestData;

namespace _42.Platform.Storyteller.Backend.Core.UnitTests.Transferring;

public class GitRepositoryTests
{
    // The history of EveryKind(), oldest first: time, then kind, then name; the deleted documents go last.
    private static readonly string[] EveryKindHistory =
    [
        "Add annotation rst.payments",
        "Add annotation sbt.northwind",
        "Add annotation usg.northwind.payments",
        "Add type schema exe (version 1)",
        "Add schema of rst.payments (version 1)",
        "Update schema of rst.payments to version 2",
        "Add template exe (version 2)",
        "Add exe schema below sbt.northwind (version 1)",
        "Update template exe to version 3",
        "Add template unt (version 1)",
        "Add schema of sbt.northwind (version 1)",
        "Add configuration of rst.cleared (version 1)",
        "Add configuration of rst.payments (version 1)",
        "Update configuration of rst.payments to version 2",
        "Add configuration of exe.northwind.payments.prod (version 1)",
        "Update type schema exe to version 2",
        "Remove deleted documents",
    ];

    // The working tree of EveryKind(), in the byte order of the paths.
    private static readonly string[] EveryKindFiles =
    [
        "annotations/rst.payments.json",
        "annotations/sbt.northwind.json",
        "annotations/usg.northwind.payments.json",
        "configurations/exe.northwind.payments.prod.conf.json",
        "configurations/rst.payments.conf.json",
        "schemas/exe.schema.json",
        "schemas/rst.payments.schema.json",
        "schemas/sbt.northwind.exe.schema.json",
        "templates/exe.template.json",
    ];

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
    public void Repository_OrdersTreesAndTheIndexLikeGit()
    {
        var zip = WriteRepository(
            (["a0.json", "a/c/d.json", "a/b.json", "a.json", "b/x.json"], false),
            (["a/c/d.json", "b/x.json"], true));

        var reader = new GitTestReader(zip);
        var history = reader.GetHistory();

        // a subtree compares as its name followed by '/': "a.json" < "a/" < "a0.json"
        reader.ReadTreeEntries(history[0].Tree).Select(entry => (entry.Mode, entry.Name)).ShouldBe(
            [("100644", "a.json"), ("40000", "a"), ("100644", "a0.json"), ("40000", "b")]);
        reader.ReadTreeEntries(reader.ReadTreeEntries(history[0].Tree)[1].Id).Select(entry => entry.Name).ShouldBe(["b.json", "c"]);
        history[0].Files.Keys.ShouldBe(["a.json", "a/b.json", "a/c/d.json", "a0.json", "b/x.json"], ignoreOrder: true);

        // the directories that the removed files leave empty are gone
        reader.ReadTreeEntries(history[1].Tree).Select(entry => entry.Name).ShouldBe(["a.json", "a", "a0.json"]);
        history[1].Files.Keys.ShouldBe(["a.json", "a/b.json", "a0.json"], ignoreOrder: true);

        // the index lists full paths in byte order
        ReadIndex(reader.Files[".git/index"]).Select(entry => entry.Path).ShouldBe(["a.json", "a/b.json", "a0.json"]);
        reader.Files.Keys.Where(name => !name.StartsWith(".git/", StringComparison.Ordinal)).ShouldBe(["a.json", "a/b.json", "a0.json"], ignoreOrder: true);
    }

    [Theory]
    [InlineData("")]
    [InlineData("/a.json")]
    [InlineData("a/")]
    [InlineData("a//b.json")]
    [InlineData("./a.json")]
    [InlineData("a/../b.json")]
    [InlineData(".git/config")]
    [InlineData("a/.GIT/b.json")]
    public void Repository_RejectsPathsThatAreNotFiles(string path)
    {
        var repository = new GitRepositoryWriter();

        Should.Throw<ArgumentException>(() => repository.Commit([(path, "{}"u8.ToArray())], GitSignature.System(Start), "Add"));
    }

    [Fact]
    public void Repository_RejectsAFileAndADirectoryOfOneName()
    {
        var repository = new GitRepositoryWriter();
        repository.Commit([("a/b.json", "{}"u8.ToArray())], GitSignature.System(Start), "Add");

        Should.Throw<InvalidOperationException>(() => repository.Commit([("a", "{}"u8.ToArray())], GitSignature.System(Start), "Add"));
        Should.Throw<InvalidOperationException>(() => repository.Commit([("a/b.json/c.json", "{}"u8.ToArray())], GitSignature.System(Start), "Add"));
    }

    [Fact]
    public void Git_WritesEveryKindInItsFolder()
    {
        var source = EveryKind();
        var reader = new GitTestReader(Write(source, ViewArchiveFormat.Git));
        var history = reader.GetHistory();

        history.Select(commit => commit.Message.Split('\n')[0]).ShouldBe(EveryKindHistory);
        history[^1].Files.Keys.ShouldBe(EveryKindFiles, ignoreOrder: true);
        reader.ReadTreeEntries(history[^1].Tree).Select(entry => (entry.Mode, entry.Name)).ShouldBe(
            [("40000", "annotations"), ("40000", "configurations"), ("40000", "schemas"), ("40000", "templates")]);

        var annotation = source.Annotations.Single(item => item.AnnotationKey == "rst.payments");
        history[^1].Files["annotations/rst.payments.json"].ShouldBe(Encoding.UTF8.GetString(ViewDocumentJson.Serialize(ViewAnnotationDocument.Write(annotation))));
        history[^1].Files["schemas/sbt.northwind.exe.schema.json"].ShouldBe("{\n  \"type\": \"object\"\n}\n");
        history[^1].Files["templates/exe.template.json"].ShouldBe("{\n  \"retries\": 3\n}\n");
        history[^1].Files["configurations/rst.payments.conf.json"].ShouldBe("{\n  \"retries\": 2\n}\n");
    }

    [Fact]
    public void Git_OrdersOneTimeLikeTheImport()
    {
        var source = Source(History("rst.a", true, (1, "a", 0, "{\"c\":1}"))) with
        {
            Annotations =
            [
                ActiveAnnotation("exe.b.a.c"),
                ActiveAnnotation("usg.b.a"),
                ActiveAnnotation("sbt.b"),
                ActiveAnnotation("rst.a"),
            ],
            Templates = [Template("exe", true, (1, "a", 0, "{\"t\":1}"))],
            Schemas =
            [
                Schema(ViewDocumentKind.AnnotationSchema, null, "rst.a", true, (1, "a", 0, "{\"s\":1}")),
                Schema(ViewDocumentKind.DescendantTypeSchema, "exe", "sbt.b", true, (1, "a", 0, "{\"s\":2}")),
                Schema(ViewDocumentKind.TypeSchema, "rst", null, true, (1, "a", 0, "{\"s\":3}")),
                Schema(ViewDocumentKind.TypeSchema, "exe", null, true, (1, "a", 0, "{\"s\":4}")),
            ],
        };

        var history = new GitTestReader(Write(source, ViewArchiveFormat.Git)).GetHistory();

        // annotations by depth (ancestors first), type and key; then type, descendant-type and annotation schemas;
        // templates; configurations
        history.Select(commit => commit.Message.TrimEnd('\n')).ShouldBe(
        [
            "Add annotation rst.a",
            "Add annotation sbt.b",
            "Add annotation usg.b.a",
            "Add annotation exe.b.a.c",
            "Add type schema exe (version 1)",
            "Add type schema rst (version 1)",
            "Add exe schema below sbt.b (version 1)",
            "Add schema of rst.a (version 1)",
            "Add template exe (version 1)",
            "Add configuration of rst.a (version 1)",
        ]);
    }

    [Fact]
    public void Git_CommitsAnAnnotationAtItsCreatedStampWithItsCurrentContent()
    {
        // a re-created annotation: its configuration has versions from the earlier lifetime
        var annotation = ActiveAnnotation("rst.a", 5, 6, "Again");
        var source = Source(
                History("rst.a", true, (1, "account: user_02", 1, "{\"a\":1}"), (2, "account: user_02", 2, "{\"a\":2}"), (3, "account: user_02", 7, "{\"a\":3}")))
            with
            {
                Annotations = [annotation],
            };

        var history = new GitTestReader(Write(source, ViewArchiveFormat.Git)).GetHistory();

        history.Select(commit => commit.Message.TrimEnd('\n')).ShouldBe(
        [
            "Add configuration of rst.a (version 1)",
            "Update configuration of rst.a to version 2",
            "Add annotation rst.a",
            "Update configuration of rst.a to version 3",
        ]);

        // Created is by an account, Modified by a machine: the commit takes Created
        history[2].Author.ShouldBe($"account: user_01 <account.user_01@2s.platform> {Start.AddMinutes(5).ToUnixTimeSeconds()} +0000");
        history[2].Committer.ShouldBe(history[2].Author);
        history[2].Files["annotations/rst.a.json"].ShouldBe(Encoding.UTF8.GetString(ViewDocumentJson.Serialize(ViewAnnotationDocument.Write(annotation))));
        history[2].Files["annotations/rst.a.json"].ShouldContain("\"Title\": \"Again\"");
        history[1].Files.Keys.ShouldBe(["configurations/rst.a.conf.json"]);
    }

    [Fact]
    public void Git_CommitsEveryVersionInTimeOrder()
    {
        var zip = Write(
            ViewArchiveFormat.Git,
            History("rst.b", true, (1, "account: user_02", 2, """{"b":1}"""), (2, "account: user_02", 10, """{"b":2}""")),
            History("rst.a", true, (1, "account: user_01", 0, """{"a":1}"""), (2, "machine: machine-1", 5, """{"a":2}""")));

        var history = new GitTestReader(zip).GetHistory();

        history.Select(commit => commit.Message).ShouldBe(
        [
            "Add configuration of rst.a (version 1)\n",
            "Add configuration of rst.b (version 1)\n",
            "Update configuration of rst.a to version 2\n",
            "Update configuration of rst.b to version 2\n",
        ]);

        history[0].Author.ShouldBe($"account: user_01 <account.user_01@2s.platform> {Start.ToUnixTimeSeconds()} +0000");
        history[2].Author.ShouldBe($"machine: machine-1 <machine.machine-1@2s.platform> {Start.AddMinutes(5).ToUnixTimeSeconds()} +0000");
        history[2].Committer.ShouldBe(history[2].Author);

        history[0].Files.Keys.ShouldBe(["configurations/rst.a.conf.json"]);
        history[1].Files.Keys.ShouldBe(["configurations/rst.a.conf.json", "configurations/rst.b.conf.json"], ignoreOrder: true);
        history[2].Files["configurations/rst.a.conf.json"].ShouldBe("{\n  \"a\": 2\n}\n");
        history[2].Files["configurations/rst.b.conf.json"].ShouldBe("{\n  \"b\": 1\n}\n");
        history[3].Files["configurations/rst.b.conf.json"].ShouldBe("{\n  \"b\": 2\n}\n");
    }

    [Fact]
    public void Git_SignsAVersionWithItsActorLikeAnAnnotation()
    {
        var first = new ConfigurationDocument
        {
            Version = 1,
            Author = "Ada",
            Actor = AuditActor.Account("user_01", "Ada"),
            CreatedAt = Start,
            Content = JObject.Parse("""{"a":1}"""),
        };
        var second = first with { Version = 2, Author = "Deployer", Actor = AuditActor.Machine("machine-1", "Deployer"), CreatedAt = Start.AddMinutes(1) };
        var third = first with { Version = 3, Author = "Ada", Actor = AuditActor.System, CreatedAt = Start.AddMinutes(2) };
        var fourth = first with { Version = 4, Author = "account: user_02", Actor = null, CreatedAt = Start.AddMinutes(3) };
        var source = Source(new ConfigurationHistory { AnnotationKey = "rst.a", Versions = [first, second, third, fourth], Current = fourth });

        var history = new GitTestReader(Write(source, ViewArchiveFormat.Git)).GetHistory();

        // the display name is a snapshot, the id names the actor; without an actor the author string signs
        history.Select(commit => commit.Author).ShouldBe(
        [
            $"account: user_01 <account.user_01@2s.platform> {Start.ToUnixTimeSeconds()} +0000",
            $"machine: machine-1 <machine.machine-1@2s.platform> {Start.AddMinutes(1).ToUnixTimeSeconds()} +0000",
            $"2S Platform <system@2s.platform> {Start.AddMinutes(2).ToUnixTimeSeconds()} +0000",
            $"account: user_02 <account.user_02@2s.platform> {Start.AddMinutes(3).ToUnixTimeSeconds()} +0000",
        ]);
        history.Select(commit => commit.Committer).ShouldBe(history.Select(commit => commit.Author));
    }

    [Fact]
    public void Git_KeepsTheVersionsOfADocumentInOrder()
    {
        var source = Source(
                History("rst.a", true, (1, "a", 0, """{"a":1}"""), (2, "a", 0, """{"a":2}"""), (3, "a", 0, """{"a":3}""")),
                History("rst.b", true, (1, "a", 5, """{"b":1}"""), (2, "a", 3, """{"b":2}""")))
            with
            {
                Templates = [Template("exe", true, (1, "a", 4, """{"t":1}"""))],
            };

        // equal times keep the version order, and so does a version older than the one before it
        new GitTestReader(Write(source, ViewArchiveFormat.Git)).GetHistory().Select(commit => commit.Message.TrimEnd('\n')).ShouldBe(
        [
            "Add configuration of rst.a (version 1)",
            "Update configuration of rst.a to version 2",
            "Update configuration of rst.a to version 3",
            "Add template exe (version 1)",
            "Add configuration of rst.b (version 1)",
            "Update configuration of rst.b to version 2",
        ]);
    }

    [Fact]
    public void Git_RemovesDeletedDocumentsInTheLastCommit()
    {
        var reader = new GitTestReader(Write(EveryKind(), ViewArchiveFormat.Git));
        var history = reader.GetHistory();

        history[^1].Message.ShouldBe(
            "Remove deleted documents\n\nschemas/sbt.northwind.schema.json\ntemplates/unt.template.json\nconfigurations/rst.cleared.conf.json\n");
        history[^1].Author.ShouldBe($"2S Platform <system@2s.platform> {Start.AddDays(1).ToUnixTimeSeconds()} +0000");
        history[^1].Committer.ShouldBe(history[^1].Author);

        history[^2].Files.Keys.ShouldContain("templates/unt.template.json");
        history[^1].Files.Keys.ShouldBe(EveryKindFiles, ignoreOrder: true);
        reader.Files.Keys.Where(name => !name.StartsWith(".git/", StringComparison.Ordinal)).ShouldBe(EveryKindFiles, ignoreOrder: true);
    }

    [Fact]
    public void Git_RemovesNothingWithoutDeletedDocuments()
    {
        var history = new GitTestReader(Write(ViewArchiveFormat.Git, History("rst.a", true, (1, "a", 0, "{\"a\":1}")))).GetHistory();

        history.Select(commit => commit.Message.TrimEnd('\n')).ShouldBe(["Add configuration of rst.a (version 1)"]);
    }

    [Fact]
    public void Git_RewritesOnlyTheTreesOnTheChangedPath()
    {
        var reader = new GitTestReader(Write(EveryKind(), ViewArchiveFormat.Git));
        var history = reader.GetHistory();

        foreach (var (previous, current) in history.Zip(history.Skip(1)))
        {
            var changedFolders = previous.Files.Keys
                .Union(current.Files.Keys)
                .Where(path => !previous.Files.TryGetValue(path, out var earlier) || !current.Files.TryGetValue(path, out var later) || earlier != later)
                .Select(path => path[..path.IndexOf('/')])
                .ToHashSet();
            var previousTrees = reader.ReadTreeEntries(previous.Tree).ToDictionary(entry => entry.Name, entry => entry.Id);

            changedFolders.ShouldNotBeEmpty();

            foreach (var entry in reader.ReadTreeEntries(current.Tree))
            {
                if (changedFolders.Contains(entry.Name))
                {
                    previousTrees.GetValueOrDefault(entry.Name).ShouldNotBe(entry.Id);
                }
                else
                {
                    previousTrees[entry.Name].ShouldBe(entry.Id, $"{entry.Name} in '{current.Message.Trim()}'");
                }
            }
        }
    }

    [Fact]
    public void Git_ChecksOutTheLastCommitWithItsIndex()
    {
        var source = Source(
                History("usg.shop.payments", true, (1, "a", 0, """{"u":1}""")),
                History("rst.a:b", true, (1, "a", 1, """{"a":1}"""), (2, "a", 2, """{"a":2}""")))
            with
            {
                Annotations = [ActiveAnnotation("rst.a", 3)],
                Templates = [Template("exe", true, (1, "a", 4, """{"t":1}"""))],
            };

        var reader = new GitTestReader(Write(source, ViewArchiveFormat.Git));
        var last = reader.GetHistory()[^1];
        var workingTree = reader.Files
            .Where(file => !file.Key.StartsWith(".git/", StringComparison.Ordinal))
            .ToDictionary(file => file.Key, file => Encoding.UTF8.GetString(file.Value));

        workingTree.ShouldBe(last.Files, ignoreOrder: true);
        workingTree.Keys.ShouldContain("configurations/rst.a%3Ab.conf.json");

        reader.Head.ShouldBe("ref: refs/heads/main\n");
        reader.Files.ShouldContainKey(".git/config");
        reader.Files.ShouldContainKey(".git/refs/tags/");

        var index = ReadIndex(reader.Files[".git/index"]);

        index.Select(entry => entry.Path).ShouldBe(
        [
            "annotations/rst.a.json",
            "configurations/rst.a%3Ab.conf.json",
            "configurations/usg.shop.payments.conf.json",
            "templates/exe.template.json",
        ]);

        foreach (var (path, id, size) in index)
        {
            var blob = reader.GetObject(id);
            blob.Type.ShouldBe("blob");
            Encoding.UTF8.GetString(blob.Content).ShouldBe(workingTree[path]);
            size.ShouldBe((uint)blob.Content.Length);
        }
    }

    [Fact]
    public void Git_WorkingTreeReadsBackAsTheCurrentDocuments()
    {
        var source = EveryKind();

        var git = ViewArchiveReader.Read(new MemoryStream(Write(source, ViewArchiveFormat.Git)), new ViewTransferOptions());
        var folder = ViewArchiveReader.Read(new MemoryStream(Write(source, ViewArchiveFormat.Folder)), new ViewTransferOptions());

        // .git is hidden, and the git format has neither version files nor export.json
        git.Files.Select(file => file.Path).ShouldBe(EveryKindFiles);
        git.IgnoredFiles.ShouldBe(0);
        git.Files.ShouldAllBe(file => file.ErrorCode == null);
        git.Files.Select(file => file.Content!.ToString()).ShouldBe(folder.Files.Select(file => file.Content!.ToString()));
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
        var history = reader.GetHistory();

        history.Count.ShouldBe(150);
        reader.DeltaCount.ShouldBeGreaterThan(200);

        // the subtree of the folder is a delta against its previous version, except where a delta chain is cut
        var folderTrees = history
            .Select(commit => reader.ReadTreeEntries(commit.Tree).Single(entry => entry.Name == ViewArchiveNames.ConfigurationsFolder).Id)
            .ToList();
        folderTrees.Count(reader.DeltaIds.Contains).ShouldBeGreaterThan(130);

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

    [Fact]
    public void Signature_NamesAnActorLikeAnAuthor()
    {
        var at = new DateTimeOffset(2026, 10, 14, 1, 30, 12, TimeSpan.Zero);
        var seconds = at.ToUnixTimeSeconds();

        GitSignature.FromActor(AuditActor.Account("user_01", "Ada"), at).Format().ShouldBe($"account: user_01 <account.user_01@2s.platform> {seconds} +0000");
        GitSignature.FromActor(AuditActor.Machine("machine-1", "Deployer"), at).Format().ShouldBe($"machine: machine-1 <machine.machine-1@2s.platform> {seconds} +0000");
        GitSignature.FromActor(AuditActor.System, at).ShouldBe(GitSignature.System(at));
        GitSignature.FromActor(null, at).ShouldBe(GitSignature.System(at));
    }

    // The real git, when it is installed, reads the exported repository like any clone.
    [Fact]
    public void GitCli_AcceptsTheRepositoryOfEveryKind()
    {
        if (!IsGitInstalled())
        {
            return;
        }

        RunInRepository(Write(EveryKind(), ViewArchiveFormat.Git), directory =>
        {
            RunGit(directory, "fsck --full --strict --no-dangling").ShouldBeEmpty();
            RunGit(directory, "status --porcelain").ShouldBeEmpty();
            RunGit(directory, "ls-files").ShouldBe(string.Join('\n', EveryKindFiles));
            RunGit(directory, "ls-tree --name-only HEAD").ShouldBe("annotations\nconfigurations\nschemas\ntemplates");
            RunGit(directory, "log --reverse --format=%s").ShouldBe(string.Join('\n', EveryKindHistory));
            RunGit(directory, "log --format=%an|%ae|%at|%s -- templates/exe.template.json").ShouldBe(
                $"account: user_02|account.user_02@2s.platform|{Start.AddMinutes(6).ToUnixTimeSeconds()}|Update template exe to version 3\n"
                + $"account: user_01|account.user_01@2s.platform|{Start.AddMinutes(4).ToUnixTimeSeconds()}|Add template exe (version 2)");
            RunGit(directory, "log --format=%an|%ae|%at|%s -- annotations/rst.payments.json").ShouldBe(
                $"account: user_01|account.user_01@2s.platform|{Start.ToUnixTimeSeconds()}|Add annotation rst.payments");
            RunGit(directory, "log -1 --format=%an|%ae|%at|%b").ShouldBe(
                $"2S Platform|system@2s.platform|{Start.AddDays(1).ToUnixTimeSeconds()}|"
                + "schemas/sbt.northwind.schema.json\ntemplates/unt.template.json\nconfigurations/rst.cleared.conf.json");
            RunGit(directory, "show HEAD:templates/exe.template.json").ShouldBe("{\n  \"retries\": 3\n}");
            RunGit(directory, "show HEAD~1:templates/unt.template.json").ShouldBe("{\n  \"kind\": \"job\"\n}");
        });
    }

    [Fact]
    public void GitCli_AcceptsTreesInGitOrder()
    {
        if (!IsGitInstalled())
        {
            return;
        }

        var zip = WriteRepository(
            (["a0.json", "a/c/d.json", "a/b.json", "a.json", "b/x.json"], false),
            (["a/c/d.json", "b/x.json"], true));

        RunInRepository(zip, directory =>
        {
            RunGit(directory, "fsck --full --strict --no-dangling").ShouldBeEmpty();
            RunGit(directory, "status --porcelain").ShouldBeEmpty();
            RunGit(directory, "ls-files").ShouldBe("a.json\na/b.json\na0.json");
            RunGit(directory, "ls-tree -r --name-only HEAD~1").ShouldBe("a.json\na/b.json\na/c/d.json\na0.json\nb/x.json");
        });
    }

    // Every kind with current documents, earlier versions and deleted documents (the template unt, the schema of
    // sbt.northwind, the configuration of rst.cleared); the arrays are not in history order.
    private static ViewArchiveSource EveryKind()
    {
        return Source(
                History("rst.payments", true, (1, "account: user_01", 10, """{"retries":1}"""), (2, "account: user_02", 12, """{"retries":2}""")),
                History("rst.cleared", false, (1, "account: user_01", 9, """{"old":true}""")),
                History("exe.northwind.payments.prod", true, (1, "machine: machine-1", 14, """{"timeout":30}""")))
            with
            {
                Annotations =
                [
                    ActiveAnnotation("usg.northwind.payments", 0, 40, "Payments of Northwind"),
                    ActiveAnnotation("sbt.northwind", 0, 30, "Northwind"),
                    ActiveAnnotation("rst.payments", 0, 20, "Payments"),
                ],
                Schemas =
                [
                    Schema(ViewDocumentKind.AnnotationSchema, null, "sbt.northwind", false, (1, "account: user_01", 8, """{"type":"object"}""")),
                    Schema(
                        ViewDocumentKind.AnnotationSchema,
                        null,
                        "rst.payments",
                        true,
                        (1, "account: user_01", 2, """{"type":"object"}"""),
                        (2, "account: user_01", 3, """{"type":"object","required":["retries"]}""")),
                    Schema(ViewDocumentKind.DescendantTypeSchema, "exe", "sbt.northwind", true, (1, "account: user_01", 5, """{"type":"object"}""")),
                    Schema(
                        ViewDocumentKind.TypeSchema,
                        "exe",
                        null,
                        true,
                        (1, "account: user_01", 1, """{"type":"object","version":1}"""),
                        (2, "account: user_02", 90, """{"type":"object","version":2}""")),
                ],
                Templates =
                [
                    Template("unt", false, (1, "account: user_01", 7, """{"kind":"job"}""")),
                    Template("exe", true, (2, "account: user_01", 4, """{"retries":2}"""), (3, "account: user_02", 6, """{"retries":3}""")),
                ],
            };
    }

    // A repository of commits by the system, a minute apart: the paths a commit adds (with their own path as the
    // content) or removes.
    private static byte[] WriteRepository(params (string[] Paths, bool Remove)[] commits)
    {
        var repository = new GitRepositoryWriter();

        for (var index = 0; index < commits.Length; index++)
        {
            var (paths, remove) = commits[index];
            repository.Commit(
                paths.Select(path => (path, remove ? null : Encoding.UTF8.GetBytes($"\"{path}\"\n"))),
                GitSignature.System(Start.AddMinutes(index)),
                remove ? "Remove" : "Add");
        }

        using var stream = new MemoryStream();

        using (var archive = new ZipArchive(stream, ZipArchiveMode.Create, leaveOpen: true))
        {
            repository.WriteTo(archive, Start.AddDays(1));
        }

        return stream.ToArray();
    }

    // The entries of .git/index (version 2), checked for their layout: path, blob id and size.
    private static List<(string Path, string Id, uint Size)> ReadIndex(byte[] index)
    {
        Encoding.ASCII.GetString(index, 0, 4).ShouldBe("DIRC");
        BinaryPrimitives.ReadUInt32BigEndian(index.AsSpan(4)).ShouldBe(2u);
        var count = (int)BinaryPrimitives.ReadUInt32BigEndian(index.AsSpan(8));
        SHA1.HashData(index.AsSpan(0, index.Length - 20)).ShouldBe(index.AsSpan(index.Length - 20).ToArray());

        var entries = new List<(string Path, string Id, uint Size)>(count);
        var position = 12;

        for (var entry = 0; entry < count; entry++)
        {
            BinaryPrimitives.ReadUInt32BigEndian(index.AsSpan(position + 24)).ShouldBe(0x81A4u);
            var size = BinaryPrimitives.ReadUInt32BigEndian(index.AsSpan(position + 36));
            var id = Convert.ToHexStringLower(index.AsSpan(position + 40, 20));
            var nameLength = BinaryPrimitives.ReadUInt16BigEndian(index.AsSpan(position + 60)) & 0xFFF;
            var name = Encoding.UTF8.GetString(index, position + 62, nameLength);

            entries.Add((name, id, size));
            position += 62 + nameLength + (8 - ((62 + nameLength) % 8));
        }

        position.ShouldBe(index.Length - 20);
        return entries;
    }

    private static void RunInRepository(byte[] zip, Action<string> action)
    {
        var directory = Directory.CreateTempSubdirectory("view-git-");

        try
        {
            ZipFile.ExtractToDirectory(new MemoryStream(zip), directory.FullName);
            action(directory.FullName);
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
