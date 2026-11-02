using System;
using System.IO;
using System.IO.Abstractions;
using Shouldly;
using Xunit;

namespace _42.Testing.System.IO.Abstractions.UnitTests;

public sealed class ReadonlyFileOpenHandleTests : IDisposable
{
    private readonly string _directory = Path.Combine(Path.GetTempPath(), "readonly-fs-" + Guid.NewGuid().ToString("N"));
    private readonly string _file;
    private readonly ReadonlyFileSystem _fileSystem = new(new FileSystem());

    public ReadonlyFileOpenHandleTests()
    {
        Directory.CreateDirectory(_directory);
        _file = Path.Combine(_directory, "existing.txt");
        File.WriteAllText(_file, "content");
    }

    [Fact]
    public void ReadOfAnExistingFile_IsAllowed()
    {
        using var handle = _fileSystem.File.OpenHandle(_file);
        _fileSystem.RandomAccess.GetLength(handle).ShouldBe(7);
    }

    [Fact]
    public void OpenOrCreateOfAnExistingFile_IsAllowed()
    {
        using var handle = _fileSystem.File.OpenHandle(_file, FileMode.OpenOrCreate);
        handle.IsInvalid.ShouldBeFalse();
    }

    [Theory]
    [InlineData(FileMode.Open, FileAccess.Write)]
    [InlineData(FileMode.Open, FileAccess.ReadWrite)]
    [InlineData(FileMode.Create, FileAccess.Read)]
    [InlineData(FileMode.Truncate, FileAccess.Read)]
    public void WritingAccessOrMode_IsRefused_AndTheFileStays(FileMode mode, FileAccess access)
    {
        Should.Throw<NotSupportedException>(() => _fileSystem.File.OpenHandle(_file, mode, access));
        File.ReadAllText(_file).ShouldBe("content");
    }

    [Fact]
    public void DeleteOnClose_IsRefused_AndTheFileStays()
    {
        Should.Throw<NotSupportedException>(() => _fileSystem.File.OpenHandle(_file, options: FileOptions.DeleteOnClose));
        File.Exists(_file).ShouldBeTrue();
    }

    [Fact]
    public void OpenOrCreateOfAMissingFile_IsRefused_AndCreatesNothing()
    {
        var missing = Path.Combine(_directory, "missing.txt");
        Should.Throw<NotSupportedException>(() => _fileSystem.File.OpenHandle(missing, FileMode.OpenOrCreate));
        File.Exists(missing).ShouldBeFalse();
    }

    [Fact]
    public void RandomAccessWrites_AreDropped()
    {
        using var handle = File.OpenHandle(_file, FileMode.Open, FileAccess.ReadWrite);
        _fileSystem.RandomAccess.Write(handle, "XX"u8, 0);
        _fileSystem.RandomAccess.SetLength(handle, 1);
        handle.Dispose();
        File.ReadAllText(_file).ShouldBe("content");
    }

    public void Dispose()
    {
        Directory.Delete(_directory, recursive: true);
    }
}
