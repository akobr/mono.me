using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Abstractions;
using System.IO.Abstractions.TestingHelpers;
using System.Text.Json;
using _42.Platform.Cli.Telemetry;
using Moq;
using Shouldly;
using Xunit;

namespace _42.Platform.Cli.UnitTests.Telemetry;

// The anonymous installation id in ~/.42for.net/telemetry.json: written once, read afterwards, never fatal.
public class InstallationIdStoreTests
{
    private static readonly string Directory = P(@"C:\Users\ada\.42for.net");
    private static readonly string FilePath = P(@"C:\Users\ada\.42for.net\telemetry.json");

    private readonly MockFileSystem _fileSystem = new(new Dictionary<string, MockFileData>(), P(@"C:\work"));

    [Fact]
    public void TheFirstCall_WritesANewId_AndTheNextCallReadsTheSameOne()
    {
        new InstallationIdStore(_fileSystem, Directory).TryGetOrCreate(out var first, out var firstCreated).ShouldBeTrue();
        new InstallationIdStore(_fileSystem, Directory).TryGetOrCreate(out var second, out var secondCreated).ShouldBeTrue();

        firstCreated.ShouldBeTrue();
        secondCreated.ShouldBeFalse();
        second.ShouldBe(first);
        Guid.TryParseExact(first, "N", out _).ShouldBeTrue();
        StoredId().ShouldBe(first);
    }

    [Theory]
    [InlineData("")]
    [InlineData("{")]
    [InlineData("null")]
    [InlineData("[]")]
    [InlineData("""{"installationId":"not-a-guid"}""")]
    [InlineData("""{"installationId":"00000000000000000000000000000000"}""")]
    [InlineData("""{"other":"6d0f2c4e00004000800000000000000a"}""")]
    public void ADamagedOrNonGuidFile_IsReplaced(string content)
    {
        _fileSystem.AddFile(FilePath, new MockFileData(content));

        new InstallationIdStore(_fileSystem, Directory).TryGetOrCreate(out var id, out var created).ShouldBeTrue();

        created.ShouldBeTrue();
        Guid.TryParseExact(id, "N", out var guid).ShouldBeTrue();
        guid.ShouldNotBe(Guid.Empty);
        StoredId().ShouldBe(id);
    }

    [Fact]
    public void AGuidInAnotherFormat_IsKept()
    {
        _fileSystem.AddFile(FilePath, new MockFileData("""{"installationId":"6d0f2c4e-0000-4000-8000-00000000000a"}"""));

        new InstallationIdStore(_fileSystem, Directory).TryGetOrCreate(out var id, out var created).ShouldBeTrue();

        created.ShouldBeFalse();
        id.ShouldBe("6d0f2c4e00004000800000000000000a");
    }

    [Fact]
    public void AnUnwritableDirectory_ReturnsFalse_WithoutThrowing()
    {
        var fileSystem = new Mock<IFileSystem> { DefaultValue = DefaultValue.Mock };
        fileSystem.Setup(system => system.Path).Returns(_fileSystem.Path);
        fileSystem.Setup(system => system.File.Exists(It.IsAny<string>())).Returns(false);
        fileSystem.Setup(system => system.Directory.CreateDirectory(It.IsAny<string>())).Throws(new UnauthorizedAccessException("Access denied."));

        new InstallationIdStore(fileSystem.Object, Directory).TryGetOrCreate(out var id, out var created).ShouldBeFalse();

        id.ShouldBeEmpty();
        created.ShouldBeFalse();
    }

    [Fact]
    public void AFileThatCannotBeRead_ReturnsFalse_WithoutThrowing()
    {
        var fileSystem = new Mock<IFileSystem> { DefaultValue = DefaultValue.Mock };
        fileSystem.Setup(system => system.Path).Returns(_fileSystem.Path);
        fileSystem.Setup(system => system.File.Exists(It.IsAny<string>())).Returns(true);
        fileSystem.Setup(system => system.File.ReadAllText(It.IsAny<string>())).Throws(new IOException("The file is locked."));

        new InstallationIdStore(fileSystem.Object, Directory).TryGetOrCreate(out _, out _).ShouldBeFalse();
    }

    private static string P(string path) => MockUnixSupport.Path(path);

    private string? StoredId()
    {
        using var document = JsonDocument.Parse(_fileSystem.File.ReadAllText(FilePath));
        return document.RootElement.GetProperty("installationId").GetString();
    }
}
