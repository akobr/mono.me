using System;
using System.IO;
using System.IO.Abstractions;
using System.Runtime.Versioning;
using _42.Platform.Cli.Output;
using Shouldly;
using Xunit;

namespace _42.Platform.Cli.UnitTests.Output;

// The --export file of a resolved read on Linux and macOS, on the real file system in a temporary directory. Windows
// has no Unix file modes, so the checks run on Unix only.
[UnsupportedOSPlatform("windows")]
public sealed class OwnerOnlyFileTests : IDisposable
{
    private const string Secret = "APP_DB__PASSWORD='s3cr3t-value'\n";

    private readonly string _directory = Path.Combine(Path.GetTempPath(), "sform-export-" + Guid.NewGuid().ToString("N"));

    public OwnerOnlyFileTests()
    {
        Directory.CreateDirectory(_directory);
    }

    [Fact]
    public void ANewFile_IsTheOwnersAlone()
    {
        if (OperatingSystem.IsWindows())
        {
            return;
        }

        var path = Path.Combine(_directory, "app.env");

        OwnerOnlyFile.WriteAllText(new FileSystem(), path, Secret);

        File.GetUnixFileMode(path).ShouldBe(UnixFileMode.UserRead | UnixFileMode.UserWrite);
        File.ReadAllBytes(path).ShouldBe(PlainOutputExtensions.Utf8.GetBytes(Secret));
        Directory.GetFiles(_directory).ShouldBe([path]);
    }

    // The earlier file is replaced, not written: a reader that opened it before, while it was readable by others, keeps
    // its old content and never sees the secret.
    [Fact]
    public void AnEarlierFile_IsReplacedByANewOne()
    {
        if (OperatingSystem.IsWindows())
        {
            return;
        }

        var path = Path.Combine(_directory, "app.env");
        File.WriteAllText(path, "OLD=1\n");
        File.SetUnixFileMode(path, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.GroupRead | UnixFileMode.OtherRead);
        using var earlier = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);

        OwnerOnlyFile.WriteAllText(new FileSystem(), path, Secret);

        File.GetUnixFileMode(path).ShouldBe(UnixFileMode.UserRead | UnixFileMode.UserWrite);
        File.ReadAllText(path).ShouldBe(Secret);
        new StreamReader(earlier).ReadToEnd().ShouldBe("OLD=1\n");
        Directory.GetFiles(_directory).ShouldBe([path]);
    }

    [Fact]
    public void AFailedWrite_LeavesNoFileBehind()
    {
        if (OperatingSystem.IsWindows())
        {
            return;
        }

        var path = Path.Combine(_directory, "app.env");
        Directory.CreateDirectory(path);

        Should.Throw<IOException>(() => OwnerOnlyFile.WriteAllText(new FileSystem(), path, Secret));

        Directory.GetFiles(_directory).ShouldBeEmpty();
    }

    public void Dispose()
    {
        if (Directory.Exists(_directory))
        {
            Directory.Delete(_directory, recursive: true);
        }
    }
}
