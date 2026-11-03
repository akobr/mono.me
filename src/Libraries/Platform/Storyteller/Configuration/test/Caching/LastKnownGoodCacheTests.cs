using System.IO.Abstractions;
using System.IO.Abstractions.TestingHelpers;
using System.Text;
using _42.Platform.Storyteller.ConfigurationFormats;
using _42.Platform.Storyteller.Sdk.Configuring.Caching;
using _42.Platform.Storyteller.Sdk.Configuring.Logging;
using _42.Platform.Storyteller.Sdk.Configuring.Reading;
using _42.Testing.System.IO.Abstractions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Time.Testing;
using Newtonsoft.Json.Linq;

namespace _42.Platform.Storyteller.Sdk.Configuring.UnitTests.Caching;

public class LastKnownGoodCacheTests
{
    private const string PrivateNoCache = "private, no-cache";
    private const string Fingerprint = "ApiKeyCredential:0123456789abcdef0123456789abcdef";

    private static readonly string CacheDirectory = MockUnixSupport.Path(@"C:\cache\storyteller");

    private readonly MockFileSystem _fileSystem = new UnixModeMockFileSystem();
    private readonly FakeTimeProvider _time = new(new DateTimeOffset(2026, 10, 23, 8, 0, 0, TimeSpan.Zero));
    private readonly RecordingLoggerFactory _logs = new();

    [Fact]
    public void AnEntry_IsReadBackAsWritten()
    {
        var cache = Create();
        var identity = Identity();

        cache.Save(identity, TestDocuments.Document(), TestDocuments.WeakTag, PrivateNoCache);
        var entry = cache.Read(identity).ShouldNotBeNull();

        entry.Identity.ShouldBe(identity.Text);
        entry.ETag.ShouldBe(TestDocuments.WeakTag);
        entry.StoredAt.ShouldBe(_time.GetUtcNow());
        entry.CacheControl.ShouldBe(PrivateNoCache);
        entry.Document.Version.ShouldBe(3);
        entry.Document.Hash.ShouldBe("a1b2c3d4");
        entry.Document.Modified.At.ShouldBe(new DateTimeOffset(2026, 10, 19, 8, 30, 0, TimeSpan.Zero));
        entry.IsProtected.ShouldBeFalse();
        _fileSystem.Directory.GetFiles(CacheDirectory).ShouldBe([cache.GetPath(identity)]);
        _logs.Entries.ShouldBeEmpty();
    }

    [Fact]
    public void TheFileName_IsAHashOfTheIdentity()
    {
        var identity = Identity();

        _fileSystem.Path.GetFileName(Create().GetPath(identity)).ShouldBe($"{identity.FileName}.cache");
        identity.FileName.ShouldMatch("^[0-9a-f]{32}$");
    }

    [Fact]
    public void AWrite_GoesToAUniqueTemporaryFile_ThenMoves()
    {
        var tracer = new FileSystemTracer();
        var cache = Create(fileSystem: new DiagnosticFileSystem(_fileSystem, tracer));
        var identity = Identity();

        cache.Save(identity, TestDocuments.Document(), TestDocuments.WeakTag, PrivateNoCache);
        cache.Save(identity, TestDocuments.Document(version: 4), TestDocuments.OtherTag, PrivateNoCache);

        var creates = tracer.Operations.Where(operation => operation is { Operation: "New", Arguments: [string, FileStreamOptions] }).ToList();
        var moves = tracer.Operations.Where(operation => operation.Operation == "Move").ToList();
        creates.Count.ShouldBe(2);
        moves.Count.ShouldBe(2);

        var temporaries = creates.Select(create => (string)create.Arguments[0]!).ToList();
        temporaries.Distinct().Count().ShouldBe(2);
        temporaries.ShouldAllBe(path => _fileSystem.Path.GetDirectoryName(path) == CacheDirectory);
        temporaries.Select(path => _fileSystem.Path.GetFileName(path)).ShouldAllBe(name => name.StartsWith(identity.FileName + ".") && name.EndsWith(".tmp"));
        creates.ShouldAllBe(create => ((FileStreamOptions)create.Arguments[1]!).Mode == FileMode.CreateNew);

        moves[0].Arguments.ShouldBe(new object?[] { temporaries[0], cache.GetPath(identity), true });
        moves[1].Arguments.ShouldBe(new object?[] { temporaries[1], cache.GetPath(identity), true });

        _fileSystem.Directory.GetFiles(CacheDirectory, "*.tmp").ShouldBeEmpty();
        cache.Read(identity)!.Document.Version.ShouldBe(4);
    }

    [Theory]
    [InlineData("no-store")]
    [InlineData("private, NO-STORE")]
    [InlineData(null)]
    public void AResolvedReadMarkedNoStoreOrWithoutCacheControl_IsNotWritten_AndDeletesTheOldEntry(string? cacheControl)
    {
        var cache = Create();
        var identity = Identity();
        cache.Save(identity, TestDocuments.Document(), TestDocuments.WeakTag, PrivateNoCache);

        cache.Save(identity, TestDocuments.Document(version: 4), TestDocuments.OtherTag, cacheControl);
        cache.Save(identity, TestDocuments.Document(version: 5), TestDocuments.OtherTag, cacheControl);

        _fileSystem.File.Exists(cache.GetPath(identity)).ShouldBeFalse();
        cache.Read(identity).ShouldBeNull();

        // Logged once per identity.
        _logs.Entries.ShouldHaveSingleItem().Level.ShouldBe(LogLevel.Information);
    }

    [Fact]
    public void TheNoStoreNotice_WaitsForALogger_ThenComesOnce()
    {
        // As at the startup: the container's logger factory is handed over later.
        var loggers = new SwitchableLoggerFactory(null);
        var cache = new LastKnownGoodCache(CacheDirectory, _fileSystem, null, _time, loggers.CreateLogger("cache"));
        var identity = Identity();

        cache.Save(identity, TestDocuments.Document(), TestDocuments.WeakTag, "no-store");
        loggers.Use(_logs);
        _logs.Entries.ShouldBeEmpty();

        cache.ReportNotCached(identity, "no-store");
        cache.Revalidated(identity, "no-store");
        cache.Save(identity, TestDocuments.Document(version: 4), TestDocuments.OtherTag, "no-store");

        var notice = _logs.Entries.ShouldHaveSingleItem();
        notice.Level.ShouldBe(LogLevel.Information);
        notice.Message.ShouldContain("not cached on disk");
    }

    [Fact]
    public void ReportNotCached_SaysNothing_ForAnAnswerThatMayBeStored()
    {
        var cache = Create();

        cache.ReportNotCached(Identity(), PrivateNoCache);
        Create(new TestCacheProtector()).ReportNotCached(Identity(), "no-store");

        _logs.Entries.ShouldBeEmpty();
    }

    [Fact]
    public void AnEffectiveReadWithoutCacheControl_IsWritten()
    {
        var cache = Create();
        var identity = Identity(StorytellerReadMode.Effective);

        cache.Save(identity, TestDocuments.Document(), TestDocuments.WeakTag, null);

        cache.Read(identity).ShouldNotBeNull();
    }

    [Theory]
    [InlineData("no-store")]
    [InlineData(null)]
    public void A304MarkedNoStore_DeletesTheOldEntry(string? cacheControl)
    {
        var cache = Create();
        var identity = Identity();
        cache.Save(identity, TestDocuments.Document(), TestDocuments.WeakTag, PrivateNoCache);

        cache.Revalidated(identity, cacheControl);

        _fileSystem.File.Exists(cache.GetPath(identity)).ShouldBeFalse();
    }

    [Fact]
    public void A304ThatMayBeStored_KeepsTheEntry()
    {
        var cache = Create();
        var identity = Identity();
        cache.Save(identity, TestDocuments.Document(), TestDocuments.WeakTag, PrivateNoCache);

        cache.Revalidated(identity, PrivateNoCache);

        cache.Read(identity).ShouldNotBeNull();
    }

    [Theory]
    [InlineData("no-store")]
    [InlineData(PrivateNoCache)]
    [InlineData(null)]
    public void WithAProtector_EveryEntryIsProtected_AndItsTagIsTrusted(string? cacheControl)
    {
        var protector = new TestCacheProtector();
        var cache = Create(protector);
        var identity = Identity();

        cache.Save(identity, TestDocuments.Document(), TestDocuments.WeakTag, cacheControl);
        cache.Revalidated(identity, cacheControl);

        var content = _fileSystem.File.ReadAllBytes(cache.GetPath(identity));
        Encoding.ASCII.GetString(content).ShouldStartWith("2S-STORYTELLER-CACHE-PROTECTED/1\n");
        Encoding.UTF8.GetString(content).ShouldNotContain("db.local");
        protector.ProtectCalls.ShouldBe(1);

        var entry = cache.Read(identity).ShouldNotBeNull();
        entry.IsProtected.ShouldBeTrue();
        entry.TrustedETag.ShouldBe(TestDocuments.WeakTag);
        entry.Document.Version.ShouldBe(3);
    }

    [Fact]
    public void APlainEntrysTag_IsNeverTrusted()
    {
        var cache = Create();
        var identity = Identity();

        cache.Save(identity, TestDocuments.Document(), TestDocuments.WeakTag, PrivateNoCache);

        var entry = cache.Read(identity).ShouldNotBeNull();
        entry.ETag.ShouldBe(TestDocuments.WeakTag);
        entry.TrustedETag.ShouldBeNull();
    }

    [Fact]
    public void WithAProtector_APlainFileIsRejected()
    {
        var identity = Identity();
        Create().Save(identity, TestDocuments.Document(), TestDocuments.WeakTag, PrivateNoCache);

        Create(new TestCacheProtector()).Read(identity).ShouldBeNull();

        _logs.Entries.ShouldHaveSingleItem().Level.ShouldBe(LogLevel.Warning);
    }

    [Fact]
    public void WithoutAProtector_AProtectedFileIsRejected()
    {
        var identity = Identity();
        Create(new TestCacheProtector()).Save(identity, TestDocuments.Document(), TestDocuments.WeakTag, PrivateNoCache);

        Create().Read(identity).ShouldBeNull();

        _logs.Entries.ShouldHaveSingleItem().Level.ShouldBe(LogLevel.Warning);
    }

    [Fact]
    public void AProtectedBlobMovedToAnotherIdentity_IsRejected()
    {
        var cache = Create(new TestCacheProtector());
        var identity = Identity();
        var other = Identity(key: "@acme.billing.default.sbt.contoso");
        cache.Save(identity, TestDocuments.Document(), TestDocuments.WeakTag, PrivateNoCache);

        _fileSystem.File.Copy(cache.GetPath(identity), cache.GetPath(other));

        cache.Read(other).ShouldBeNull();
        _logs.Entries.ShouldHaveSingleItem().Message.ShouldContain("could not be unprotected");
    }

    [Fact]
    public void APlainFileOfAnotherIdentity_IsIgnored()
    {
        var cache = Create();
        var identity = Identity();
        var other = Identity(key: "@acme.billing.default.sbt.contoso");
        cache.Save(identity, TestDocuments.Document(), TestDocuments.WeakTag, PrivateNoCache);

        _fileSystem.File.Copy(cache.GetPath(identity), cache.GetPath(other));

        cache.Read(other).ShouldBeNull();
        _logs.Entries.ShouldHaveSingleItem().Message.ShouldContain("another identity");
    }

    [Fact]
    public void ANoStoreAnswer_LeavesAProtectedEntryOfAnotherProcess()
    {
        var identity = Identity();
        Create(new TestCacheProtector()).Save(identity, TestDocuments.Document(), TestDocuments.WeakTag, "no-store");

        var cache = Create();
        cache.Save(identity, TestDocuments.Document(), TestDocuments.WeakTag, "no-store");
        cache.Revalidated(identity, "no-store");

        _fileSystem.File.Exists(cache.GetPath(identity)).ShouldBeTrue();
    }

    [Fact]
    public void TheCredentialFingerprint_SeparatesEntries()
    {
        var cache = Create();
        var identity = Identity(fingerprint: "ApiKeyCredential:aaaa");
        var other = Identity(fingerprint: "ApiKeyCredential:bbbb");

        cache.Save(identity, TestDocuments.Document(), TestDocuments.WeakTag, PrivateNoCache);

        other.FileName.ShouldNotBe(identity.FileName);
        cache.Read(other).ShouldBeNull();
        cache.Read(identity).ShouldNotBeNull();
    }

    [Fact]
    public void DateLikeStringsAndNumbers_FlattenTheSame_FromTheCacheAsFromThePlatform()
    {
        const string content = """
            {
              "renewal": "2026-10-20T12:00:00.1230000+02:00",
              "day": "2026-10-20",
              "offset": "/Date(1224043200000)/",
              "ratio": 1.50,
              "port": 5432,
              "enabled": true
            }
            """;
        var document = TestDocuments.Document(content);
        var cache = Create();
        var identity = Identity();

        cache.Save(identity, document, TestDocuments.WeakTag, PrivateNoCache);
        var cached = cache.Read(identity).ShouldNotBeNull();

        var fromPlatform = DocumentFlattener.Flatten((JToken)document.Content);
        var fromCache = DocumentFlattener.Flatten((JToken)cached.Document.Content);
        fromCache.ShouldBe(fromPlatform, ignoreOrder: true);
        fromCache["renewal"].ShouldBe("2026-10-20T12:00:00.1230000+02:00");
        fromCache["offset"].ShouldBe("/Date(1224043200000)/");
        fromCache["ratio"].ShouldBe("1.5");
    }

    [Theory]
    [InlineData("")]
    [InlineData("not json")]
    [InlineData("[1, 2]")]
    [InlineData("""{ "format": 2, "identity": "x", "storedAt": "2026-10-23T08:00:00Z", "document": {} }""")]
    [InlineData("""{ "format": "one" }""")]
    [InlineData("""{ "format": 9999999999, "identity": "x", "storedAt": "2026-10-23T08:00:00Z", "document": {} }""")]
    [InlineData("""{ "format": 1e20, "identity": "x", "storedAt": "2026-10-23T08:00:00Z", "document": {} }""")]
    [InlineData("""{ "format": 1, "storedAt": "2026-10-23T08:00:00Z", "document": {} }""")]
    [InlineData("""{ "format": 1, "identity": "x", "storedAt": "yesterday", "document": {} }""")]
    [InlineData("""{ "format": 1, "identity": "x", "storedAt": "2026-10-23T08:00:00Z", "document": "text" }""")]
    public void CorruptFiles_AreIgnored(string text)
    {
        var cache = Create();
        var identity = Identity();
        _fileSystem.AddFile(cache.GetPath(identity), new MockFileData(text));

        cache.Read(identity).ShouldBeNull();

        _logs.Entries.ShouldHaveSingleItem().Level.ShouldBe(LogLevel.Warning);
    }

    [Fact]
    public void BytesThatAreNotUtf8_AreIgnored()
    {
        var cache = Create();
        var identity = Identity();
        _fileSystem.AddFile(cache.GetPath(identity), new MockFileData([0x7B, 0xFF, 0xFE, 0x7D]));

        cache.Read(identity).ShouldBeNull();
    }

    [Fact]
    public void TwoCaches_ShareOneIdentityOnOneFileSystem()
    {
        var first = Create();
        var second = Create();
        var identity = Identity();

        first.Save(identity, TestDocuments.Document(version: 1), TestDocuments.WeakTag, PrivateNoCache);
        second.Save(identity, TestDocuments.Document(version: 2), TestDocuments.OtherTag, PrivateNoCache);

        first.Read(identity)!.Document.Version.ShouldBe(2);
        second.Read(identity)!.ETag.ShouldBe(TestDocuments.OtherTag);
        _fileSystem.Directory.GetFiles(CacheDirectory).ShouldHaveSingleItem();
    }

    [Fact]
    public async Task TwoCaches_WriteOneIdentityConcurrently_OnARealFileSystem()
    {
        var fileSystem = new FileSystem();
        var directory = fileSystem.Path.Combine(fileSystem.Path.GetTempPath(), $"storyteller-cache-{Guid.NewGuid():N}");

        try
        {
            var caches = new[] { Create(fileSystem: fileSystem, directory: directory), Create(fileSystem: fileSystem, directory: directory) };
            var identity = Identity();

            await Task.WhenAll(Enumerable.Range(0, 40).Select(index => Task.Run(() =>
                caches[index % 2].Save(identity, TestDocuments.Document(version: index), $"W/\"r.{index}\"", PrivateNoCache))));

            var entry = caches[0].Read(identity).ShouldNotBeNull();
            entry.ETag.ShouldBe($"W/\"r.{entry.Document.Version}\"");
            fileSystem.Directory.GetFiles(directory, "*.tmp").ShouldBeEmpty();

            // A move that lost a race is logged, never thrown, and never leaves a broken entry.
            _logs.Entries.ShouldAllBe(log => log.Level == LogLevel.Warning);
        }
        finally
        {
            fileSystem.Directory.Delete(directory, true);
        }
    }

    [Theory]
    [InlineData("Move")]
    [InlineData("CreateDirectory")]
    public void AWriteFailure_IsLoggedAndNotThrown_AndTheNextWriteTriesAgain(string operation)
    {
        var tracer = new FileSystemTracer { Fail = (name, _) => name == operation ? new IOException("The disk is full.") : null };
        var cache = Create(fileSystem: new DiagnosticFileSystem(_fileSystem, tracer));
        var identity = Identity();

        cache.Save(identity, TestDocuments.Document(), TestDocuments.WeakTag, PrivateNoCache);

        _logs.Entries.ShouldHaveSingleItem().Level.ShouldBe(LogLevel.Warning);
        _fileSystem.File.Exists(cache.GetPath(identity)).ShouldBeFalse();

        if (_fileSystem.Directory.Exists(CacheDirectory))
        {
            _fileSystem.Directory.GetFiles(CacheDirectory).ShouldBeEmpty();
        }

        tracer.Fail = null;
        cache.Save(identity, TestDocuments.Document(), TestDocuments.WeakTag, PrivateNoCache);

        cache.Read(identity).ShouldNotBeNull();
    }

    [Fact]
    public void AReadFailure_IsLoggedAndNotThrown()
    {
        var identity = Identity();
        Create().Save(identity, TestDocuments.Document(), TestDocuments.WeakTag, PrivateNoCache);
        var tracer = new FileSystemTracer { Fail = (name, arguments) => name == "New" ? new UnauthorizedAccessException("Denied.") : null };

        Create(fileSystem: new DiagnosticFileSystem(_fileSystem, tracer)).Read(identity).ShouldBeNull();

        _logs.Entries.ShouldHaveSingleItem().Level.ShouldBe(LogLevel.Warning);
    }

    [Fact]
    public void ADeleteFailure_IsLoggedAndNotThrown()
    {
        var identity = Identity();
        Create().Save(identity, TestDocuments.Document(), TestDocuments.WeakTag, PrivateNoCache);
        var tracer = new FileSystemTracer { Fail = (name, _) => name == "Delete" ? new IOException("Locked.") : null };

        Create(fileSystem: new DiagnosticFileSystem(_fileSystem, tracer)).Revalidated(identity, "no-store");

        _logs.Entries.Count(log => log.Level == LogLevel.Warning).ShouldBe(1);
        _fileSystem.File.Exists(Create().GetPath(identity)).ShouldBeTrue();
    }

    [Fact]
    public void AProtectorThatFails_IsLoggedAndNotThrown()
    {
        var cache = Create(new FailingProtector());

        cache.Save(Identity(), TestDocuments.Document(), TestDocuments.WeakTag, PrivateNoCache);

        _logs.Entries.ShouldHaveSingleItem().Level.ShouldBe(LogLevel.Warning);
        _fileSystem.Directory.Exists(CacheDirectory).ShouldBeFalse();
    }

    [Fact]
    public void StaleTemporaryFiles_OfThisCache_AreRemovedOnFirstUse()
    {
        var stale = _fileSystem.Path.Combine(CacheDirectory, $"{new string('a', 32)}.{new string('b', 32)}.tmp");
        var fresh = _fileSystem.Path.Combine(CacheDirectory, $"{new string('c', 32)}.{new string('d', 32)}.tmp");
        var foreign = _fileSystem.Path.Combine(CacheDirectory, "download.tmp");

        foreach (var path in new[] { stale, fresh, foreign })
        {
            _fileSystem.AddFile(path, new MockFileData("partial"));
            _fileSystem.File.SetLastWriteTimeUtc(path, _time.GetUtcNow().UtcDateTime.AddHours(-2));
        }

        _fileSystem.File.SetLastWriteTimeUtc(fresh, _time.GetUtcNow().UtcDateTime.AddMinutes(-10));

        Create().Read(Identity()).ShouldBeNull();

        _fileSystem.File.Exists(stale).ShouldBeFalse();
        _fileSystem.File.Exists(fresh).ShouldBeTrue();
        _fileSystem.File.Exists(foreign).ShouldBeTrue();
    }

    [Fact]
    public void ARemovedDirectory_IsCreatedAgainByTheNextWrite()
    {
        var cache = Create();
        var identity = Identity();
        cache.Save(identity, TestDocuments.Document(), TestDocuments.WeakTag, PrivateNoCache);
        _fileSystem.Directory.Delete(CacheDirectory, true);

        cache.Save(identity, TestDocuments.Document(version: 4), TestDocuments.OtherTag, PrivateNoCache);

        cache.Read(identity)!.Document.Version.ShouldBe(4);
        _logs.Entries.ShouldBeEmpty();
    }

    [Fact]
    public void AReadOfAMissingDirectory_CreatesNothing()
    {
        Create().Read(Identity()).ShouldBeNull();

        _fileSystem.Directory.Exists(CacheDirectory).ShouldBeFalse();
        _logs.Entries.ShouldBeEmpty();
    }

    [UnixFact]
    public void OnUnix_TheDirectoryAndTheEntriesArePrivate()
    {
        var fileSystem = new FileSystem();
        var directory = fileSystem.Path.Combine(fileSystem.Path.GetTempPath(), $"storyteller-cache-{Guid.NewGuid():N}", "nested");

        try
        {
            var cache = Create(fileSystem: fileSystem, directory: directory);
            var identity = Identity();

            cache.Save(identity, TestDocuments.Document(), TestDocuments.WeakTag, PrivateNoCache);

            if (!OperatingSystem.IsWindows())
            {
                fileSystem.File.GetUnixFileMode(directory).ShouldBe(UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
                fileSystem.File.GetUnixFileMode(cache.GetPath(identity)).ShouldBe(UnixFileMode.UserRead | UnixFileMode.UserWrite);
            }
        }
        finally
        {
            fileSystem.Directory.Delete(fileSystem.Path.GetDirectoryName(directory)!, true);
        }
    }

    [UnixFact]
    public void OnUnix_ASharedDirectory_IsUsedWithAWarning()
    {
        var fileSystem = new FileSystem();
        var directory = fileSystem.Path.Combine(fileSystem.Path.GetTempPath(), $"storyteller-cache-{Guid.NewGuid():N}");

        try
        {
            fileSystem.Directory.CreateDirectory(directory);

            if (!OperatingSystem.IsWindows())
            {
                fileSystem.File.SetUnixFileMode(directory, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute | UnixFileMode.GroupWrite | UnixFileMode.GroupRead | UnixFileMode.GroupExecute);
            }

            var cache = Create(fileSystem: fileSystem, directory: directory);
            var identity = Identity();
            cache.Save(identity, TestDocuments.Document(), TestDocuments.WeakTag, PrivateNoCache);

            cache.Read(identity).ShouldNotBeNull();
            _logs.Entries.ShouldHaveSingleItem().Message.ShouldContain("writable by its group or by others");
        }
        finally
        {
            fileSystem.Directory.Delete(directory, true);
        }
    }

    private static CacheIdentity Identity(
        StorytellerReadMode mode = StorytellerReadMode.Resolved,
        string key = TestDocuments.Key,
        string fingerprint = Fingerprint)
    {
        return new CacheIdentity(DocumentIdentity.Create(new Uri(TestDocuments.Endpoint), FullKey.Parse(key), mode), fingerprint);
    }

    private LastKnownGoodCache Create(IStorytellerCacheProtector? protector = null, IFileSystem? fileSystem = null, string? directory = null)
    {
        return new LastKnownGoodCache(directory ?? CacheDirectory, fileSystem ?? _fileSystem, protector, _time, _logs.CreateLogger("cache"));
    }

    private sealed class FailingProtector : IStorytellerCacheProtector
    {
        public byte[] Protect(byte[] plaintext, string identity)
        {
            throw new InvalidOperationException("The key ring is unavailable.");
        }

        public byte[] Unprotect(byte[] protectedData, string identity)
        {
            throw new InvalidOperationException("The key ring is unavailable.");
        }
    }
}
