using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Abstractions;
using System.IO.Abstractions.TestingHelpers;
using System.Linq;
using _42.Platform.Cli.Configuration;
using Moq;
using Newtonsoft.Json.Linq;
using Shouldly;
using Xunit;

namespace _42.Platform.Cli.UnitTests.Configuration;

// sform.settings.json in ~/.42for.net: what is read, what a change writes, and how the files of earlier versions next to the
// assembly are read and moved in.
public class UserSettingsStoreTests
{
    // What the old EditorService wrote: the enum as a number, the read-only IsConfigured and a null command.
    internal const string LegacyEditorJson = """
        {
          "editor": {
            "EditorType": 1,
            "CustomCommand": null,
            "IsConfigured": true
          }
        }
        """;

    // What the old sform account set wrote: PascalCase names, nulls included.
    internal const string LegacyAccessJson = """
        {
          "access": {
            "OrganizationName": "contoso",
            "ProjectName": "payments",
            "ViewName": null
          }
        }
        """;

    internal static readonly string SettingsDirectory = P(@"C:\Users\ada\.42for.net");
    internal static readonly string SettingsPath = P(@"C:\Users\ada\.42for.net\sform.settings.json");
    internal static readonly string LegacyAccessPath = P(@"C:\tools\.store\42.platform.cli\1.0.0\access.default.json");
    internal static readonly string LegacyEditorPath = P(@"C:\tools\.store\42.platform.cli\1.0.0\editor.config.json");

    private readonly MockFileSystem _fileSystem = new(new Dictionary<string, MockFileData>(), P(@"C:\work"));

    [Fact]
    public void AMissingFile_LoadsAsEmpty()
    {
        CreateStore().Load().ShouldBeJson("{}");
    }

    [Theory]
    [InlineData("")]
    [InlineData("  \n")]
    public void ABlankFile_LoadsAsEmpty(string content)
    {
        _fileSystem.AddFile(SettingsPath, new MockFileData(content));

        CreateStore().Load().HasValues.ShouldBeFalse();
    }

    [Theory]
    [InlineData("{", "")]
    [InlineData("[]", "it is no JSON object.")]
    [InlineData("""{"editor":"vim"}""", "'editor' is no JSON object.")]
    [InlineData("""{"Access":[]}""", "'access' is no JSON object.")]
    [InlineData("""{"editor":{}} trailing""", "Additional text")]
    public void ADamagedFile_Throws_WithThePathAndTheReason(string content, string reason)
    {
        _fileSystem.AddFile(SettingsPath, new MockFileData(content));

        var exception = Should.Throw<UserSettingsException>(() => CreateStore().Load());

        exception.IsDamaged.ShouldBeTrue();
        exception.ExitCode.ShouldBe(ExitCodes.ERROR_WRONG_INPUT);
        exception.Message.ShouldStartWith($"The settings file {SettingsPath} is damaged: ");
        exception.Message.ShouldContain(reason);
    }

    // Comments are no damage, before the object too; a write does not keep them.
    [Fact]
    public void Comments_AreRead()
    {
        _fileSystem.AddFile(SettingsPath, new MockFileData("// my sform settings\n{ /* the editor */ \"editor\": { \"editorType\": \"Vim\" } } // end"));

        CreateStore().Read()[ConfigurationSections.EDITOR].Values.ShouldBeJson("""{"editorType":"Vim"}""");
    }

    // A link to dotfiles that are not checked out yet: no settings, not a failure of every run.
    [Fact]
    public void ALinkToAMissingFile_LoadsAsEmpty()
    {
        var file = ForwardingFile();
        file.Setup(f => f.Exists(SettingsPath)).Returns(true);
        file.Setup(f => f.ReadAllText(SettingsPath)).Throws(new FileNotFoundException("Could not find file."));

        new UserSettingsStore(FileSystemWith(file), SettingsPath, LegacyPaths()).Load().HasValues.ShouldBeFalse();
    }

    // Dotfiles: the settings file is a link, and stays one; the file it points to is replaced.
    [Fact]
    public void ALinkToAnExistingFile_StaysALink()
    {
        var target = P(@"C:\Users\ada\dotfiles\sform.settings.json");
        _fileSystem.AddFile(target, new MockFileData("""{"editor":{"editorType":"Vim"}}"""));
        _fileSystem.Directory.CreateDirectory(SettingsDirectory);
        _fileSystem.File.CreateSymbolicLink(SettingsPath, target);

        CreateStore().Update(ConfigurationSections.EDITOR, Changes(UserSettingsSchema.EDITOR_TYPE, "Neovim"));

        _fileSystem.FileInfo.New(SettingsPath).LinkTarget.ShouldBe(target);
        JObject.Parse(_fileSystem.File.ReadAllText(target)).ShouldBeJson("""{"editor":{"editorType":"Neovim"}}""");
    }

    // The link made before the dotfiles exist: the first change creates the file it points to, and its folder.
    [Fact]
    public void ALinkToAMissingFile_IsCreatedThroughTheLink()
    {
        var target = P(@"C:\Users\ada\dotfiles\sform\sform.settings.json");
        _fileSystem.Directory.CreateDirectory(SettingsDirectory);

        // The mock file system links only to a file that exists, so the target goes after the link is made.
        _fileSystem.AddFile(target, new MockFileData(string.Empty));
        _fileSystem.File.CreateSymbolicLink(SettingsPath, target);
        _fileSystem.Directory.Delete(P(@"C:\Users\ada\dotfiles\sform"), recursive: true);

        CreateStore().Update(ConfigurationSections.EDITOR, Changes(UserSettingsSchema.EDITOR_TYPE, "Vim"));

        _fileSystem.FileInfo.New(SettingsPath).LinkTarget.ShouldBe(target);
        JObject.Parse(_fileSystem.File.ReadAllText(target)).ShouldBeJson("""{"editor":{"editorType":"Vim"}}""");
    }

    // A loop of links is never written over: the change fails and both links stay.
    [Fact]
    public void ALoopOfLinks_FailsWithoutReplacingALink()
    {
        var other = P(@"C:\Users\ada\dotfiles\sform.settings.json");
        _fileSystem.AddFile(other, new MockFileData(string.Empty));
        _fileSystem.Directory.CreateDirectory(SettingsDirectory);
        _fileSystem.File.CreateSymbolicLink(SettingsPath, other);
        _fileSystem.File.Delete(other);
        _fileSystem.File.CreateSymbolicLink(other, SettingsPath);

        var exception = Should.Throw<UserSettingsException>(() => CreateStore().Update(ConfigurationSections.EDITOR, Changes(UserSettingsSchema.EDITOR_TYPE, "Vim")));

        exception.Message.ShouldContain("cannot be written");
        _fileSystem.FileInfo.New(SettingsPath).LinkTarget.ShouldBe(other);
        _fileSystem.FileInfo.New(other).LinkTarget.ShouldBe(SettingsPath);
    }

    [Fact]
    public void ADamagedFile_IsNeverOverwritten()
    {
        _fileSystem.AddFile(SettingsPath, new MockFileData("{ \"editor\": "));

        Should.Throw<UserSettingsException>(() => CreateStore().Update(ConfigurationSections.EDITOR, Changes(UserSettingsSchema.EDITOR_TYPE, "Vim")));

        _fileSystem.File.ReadAllText(SettingsPath).ShouldBe("{ \"editor\": ");
    }

    [Fact]
    public void ASectionOfNull_IsNoSection()
    {
        _fileSystem.AddFile(SettingsPath, new MockFileData("""{"editor":null}"""));

        CreateStore().Read()[ConfigurationSections.EDITOR].Layer.ShouldBe(UserSettingsLayer.None);
    }

    [Fact]
    public void Update_CreatesTheFolder_AndWritesTheSectionInCamelCase()
    {
        CreateStore().Update(ConfigurationSections.EDITOR, UserSettingsSchema.EditorChanges(new EditorOptions { EditorType = EditorType.Custom, CustomCommand = "code --wait" }));

        Stored().ShouldBeJson("""{"editor":{"editorType":"Custom","customCommand":"code --wait"}}""");
        _fileSystem.Directory.GetFiles(SettingsDirectory).ShouldBe([SettingsPath]);
    }

    [Fact]
    public void Update_KeepsEveryOtherPropertyAndSection()
    {
        _fileSystem.AddFile(SettingsPath, new MockFileData("""
            {
              "future": { "colors": false },
              "editor": { "editorType": "Vim", "note": "mine" },
              "access": { "projectName": "payments" }
            }
            """));

        CreateStore().Update(ConfigurationSections.EDITOR, Changes(UserSettingsSchema.EDITOR_TYPE, "Neovim"));

        Stored().ShouldBeJson("""
            {
              "future": { "colors": false },
              "editor": { "editorType": "Neovim", "note": "mine" },
              "access": { "projectName": "payments" }
            }
            """);
    }

    [Fact]
    public void Update_ReplacesAPropertyWhateverTheCaseOfItsName()
    {
        _fileSystem.AddFile(SettingsPath, new MockFileData("""{"Editor":{"EditorType":"Vim","CUSTOMCOMMAND":"x"}}"""));

        CreateStore().Update(ConfigurationSections.EDITOR, Changes(UserSettingsSchema.EDITOR_TYPE, "VsCode"));

        Stored().ShouldBeJson("""{"editor":{"editorType":"VsCode","CUSTOMCOMMAND":"x"}}""");
    }

    [Fact]
    public void Update_RemovesAPropertyForNull_AndAnEmptiedSection()
    {
        _fileSystem.AddFile(SettingsPath, new MockFileData("""{"editor":{"editorType":"Vim"},"access":{"viewName":"preview"}}"""));

        CreateStore().Update(ConfigurationSections.EDITOR, Changes(UserSettingsSchema.EDITOR_TYPE, null));

        Stored().ShouldBeJson("""{"access":{"viewName":"preview"}}""");
    }

    [Fact]
    public void Update_LeavesNoTemporaryFile()
    {
        var store = CreateStore();

        store.Update(ConfigurationSections.EDITOR, Changes(UserSettingsSchema.EDITOR_TYPE, "Vim"));
        store.Update(ConfigurationSections.EDITOR, Changes(UserSettingsSchema.EDITOR_TYPE, "Neovim"));

        _fileSystem.Directory.GetFiles(SettingsDirectory).ShouldBe([SettingsPath]);
    }

    [Fact]
    public void LoadLegacy_NormalisesTheOldEditorFile()
    {
        _fileSystem.AddFile(LegacyEditorPath, new MockFileData(LegacyEditorJson));

        CreateStore().LoadLegacy(ConfigurationSections.EDITOR).ShouldBeJson("""{"editorType":"Neovim"}""");
    }

    [Fact]
    public void LoadLegacy_NormalisesTheOldAccessFile()
    {
        _fileSystem.AddFile(LegacyAccessPath, new MockFileData(LegacyAccessJson));

        CreateStore().LoadLegacy(ConfigurationSections.ACCESS).ShouldBeJson("""{"organizationName":"contoso","projectName":"payments"}""");
    }

    [Theory]
    [InlineData("{")]
    [InlineData("")]
    [InlineData("""{"editor":{"EditorType":42}}""")]
    [InlineData("""{"other":{}}""")]
    public void LoadLegacy_IgnoresADamagedOrUselessFile(string content)
    {
        _fileSystem.AddFile(LegacyEditorPath, new MockFileData(content));

        CreateStore().LoadLegacy(ConfigurationSections.EDITOR).ShouldBeNull();
    }

    [Fact]
    public void TheFirstUpdate_MovesBothLegacySections_AndDeletesTheOldFiles()
    {
        _fileSystem.AddFile(LegacyEditorPath, new MockFileData(LegacyEditorJson));
        _fileSystem.AddFile(LegacyAccessPath, new MockFileData(LegacyAccessJson));

        CreateStore().Update(ConfigurationSections.ACCESS, Changes(UserSettingsSchema.VIEW_NAME, "preview"));

        Stored().ShouldBeJson("""
            {
              "access": { "organizationName": "contoso", "projectName": "payments", "viewName": "preview" },
              "editor": { "editorType": "Neovim" }
            }
            """);
        _fileSystem.File.Exists(LegacyEditorPath).ShouldBeFalse();
        _fileSystem.File.Exists(LegacyAccessPath).ShouldBeFalse();
    }

    [Fact]
    public void Migration_NeverOverwritesASectionTheSettingsFileHas()
    {
        _fileSystem.AddFile(SettingsPath, new MockFileData("""{"editor":{}}"""));
        _fileSystem.AddFile(LegacyEditorPath, new MockFileData(LegacyEditorJson));

        CreateStore().Update(ConfigurationSections.ACCESS, Changes(UserSettingsSchema.VIEW_NAME, "preview"));

        Stored().ShouldBeJson("""{"editor":{},"access":{"viewName":"preview"}}""");
        _fileSystem.File.Exists(LegacyEditorPath).ShouldBeTrue();
    }

    // A legacy file written by hand with more than its section stays; the settings file shadows its section.
    [Fact]
    public void ALegacyFileWithMoreThanItsSection_IsMovedButKept()
    {
        const string legacy = """{"access":{"OrganizationName":"contoso","ProjectName":"payments"},"general":{"baseUrl":"https://my-api"}}""";
        _fileSystem.AddFile(LegacyAccessPath, new MockFileData(legacy));

        CreateStore().Update(ConfigurationSections.EDITOR, Changes(UserSettingsSchema.EDITOR_TYPE, "Vim"));

        Stored().ShouldBeJson("""{"access":{"organizationName":"contoso","projectName":"payments"},"editor":{"editorType":"Vim"}}""");
        _fileSystem.File.ReadAllText(LegacyAccessPath).ShouldBe(legacy);
    }

    [Fact]
    public void ADamagedLegacyFile_IsNeitherMovedNorDeleted()
    {
        _fileSystem.AddFile(LegacyEditorPath, new MockFileData("{"));

        CreateStore().Update(ConfigurationSections.ACCESS, Changes(UserSettingsSchema.VIEW_NAME, "preview"));

        Stored().ShouldBeJson("""{"access":{"viewName":"preview"}}""");
        _fileSystem.File.Exists(LegacyEditorPath).ShouldBeTrue();
    }

    // A read-only install folder: the settings file shadows the old file from now on, even when a later change empties it.
    [Fact]
    public void AnUndeletableLegacyFile_StaysShadowed()
    {
        _fileSystem.AddFile(LegacyEditorPath, new MockFileData(LegacyEditorJson));
        var store = new UserSettingsStore(UndeletableLegacyFiles(), SettingsPath, LegacyPaths());

        store.Update(ConfigurationSections.EDITOR, Changes(UserSettingsSchema.EDITOR_TYPE, null));

        _fileSystem.File.Exists(LegacyEditorPath).ShouldBeTrue();
        Stored().ShouldBeJson("""{"editor":{}}""");
        var editor = store.Read()[ConfigurationSections.EDITOR];
        editor.Layer.ShouldBe(UserSettingsLayer.Settings);
        editor.Values.HasValues.ShouldBeFalse();
    }

    [Fact]
    public void Read_GivesEachSectionItsLayer()
    {
        _fileSystem.AddFile(SettingsPath, new MockFileData("""{"editor":{"EDITORTYPE":"vim","customCommand":"  ","unknown":1}}"""));
        _fileSystem.AddFile(LegacyEditorPath, new MockFileData(LegacyEditorJson));
        _fileSystem.AddFile(LegacyAccessPath, new MockFileData(LegacyAccessJson));

        var snapshot = CreateStore().Read();

        snapshot[ConfigurationSections.EDITOR].Layer.ShouldBe(UserSettingsLayer.Settings);
        snapshot[ConfigurationSections.EDITOR].Values.ShouldBeJson("""{"editorType":"Vim"}""");
        snapshot[ConfigurationSections.ACCESS].Layer.ShouldBe(UserSettingsLayer.Legacy);
        snapshot.Sections.Select(section => section.Name).ShouldBe([ConfigurationSections.ACCESS, ConfigurationSections.EDITOR]);
    }

    [Fact]
    public void Read_WithoutAnyFile_HasNoLayer()
    {
        CreateStore().Read().Sections.ShouldAllBe(section => section.Layer == UserSettingsLayer.None && !section.Values.HasValues);
    }

    [Fact]
    public void ADateLikeValue_StaysAsWritten()
    {
        _fileSystem.AddFile(SettingsPath, new MockFileData("""{"access":{"viewName":"2026-11-08T10:00:00Z"}}"""));
        var store = CreateStore();

        store.Update(ConfigurationSections.EDITOR, Changes(UserSettingsSchema.EDITOR_TYPE, "Vim"));

        store.Read()[ConfigurationSections.ACCESS].Values[UserSettingsSchema.VIEW_NAME]!.ToString().ShouldBe("2026-11-08T10:00:00Z");
        _fileSystem.File.ReadAllText(SettingsPath).ShouldContain("\"2026-11-08T10:00:00Z\"");
    }

    [Fact]
    public void AWriteFailure_BecomesAUserSettingsException_WithThePath()
    {
        var fileSystem = new Mock<IFileSystem> { DefaultValue = DefaultValue.Mock };
        fileSystem.Setup(system => system.Path).Returns(_fileSystem.Path);
        fileSystem.Setup(system => system.File.Exists(It.IsAny<string>())).Returns(false);
        fileSystem.Setup(system => system.Directory.CreateDirectory(It.IsAny<string>())).Throws(new UnauthorizedAccessException("Access denied."));

        var exception = Should.Throw<UserSettingsException>(() => new UserSettingsStore(fileSystem.Object, SettingsPath, LegacyPaths())
            .Update(ConfigurationSections.EDITOR, Changes(UserSettingsSchema.EDITOR_TYPE, "Vim")));

        exception.Message.ShouldBe($"The settings file {SettingsPath} cannot be written: Access denied.");
        exception.IsDamaged.ShouldBeFalse();
        exception.ExitCode.ShouldBe(ExitCodes.ERROR_CRASH);
    }

    [Fact]
    public void AReadFailure_BecomesAUserSettingsException()
    {
        var fileSystem = new Mock<IFileSystem> { DefaultValue = DefaultValue.Mock };
        fileSystem.Setup(system => system.File.Exists(SettingsPath)).Returns(true);
        fileSystem.Setup(system => system.File.ReadAllText(SettingsPath)).Throws(new IOException("The file is locked."));

        var exception = Should.Throw<UserSettingsException>(() => new UserSettingsStore(fileSystem.Object, SettingsPath, LegacyPaths()).Load());

        exception.Message.ShouldBe($"The settings file {SettingsPath} cannot be read: The file is locked.");
        exception.IsDamaged.ShouldBeFalse();
        exception.ExitCode.ShouldBe(ExitCodes.ERROR_CRASH);
    }

    // The write itself: a full disk, a read-only settings file. Nothing half written stays.
    [Fact]
    public void AFailedWrite_BecomesAUserSettingsException_AndLeavesNoTemporaryFile()
    {
        _fileSystem.AddFile(SettingsPath, new MockFileData("""{"editor":{"editorType":"Vim"}}"""));
        var file = ForwardingFile();
        file.Setup(f => f.Move(It.IsAny<string>(), SettingsPath, true)).Throws(new IOException("There is not enough space on the disk."));

        var exception = Should.Throw<UserSettingsException>(() => new UserSettingsStore(FileSystemWith(file), SettingsPath, LegacyPaths())
            .Update(ConfigurationSections.EDITOR, Changes(UserSettingsSchema.EDITOR_TYPE, "Neovim")));

        exception.Message.ShouldBe($"The settings file {SettingsPath} cannot be written: There is not enough space on the disk.");
        exception.ExitCode.ShouldBe(ExitCodes.ERROR_CRASH);
        Stored().ShouldBeJson("""{"editor":{"editorType":"Vim"}}""");
        _fileSystem.Directory.GetFiles(SettingsDirectory).ShouldBe([SettingsPath]);
    }

    // Another sform run holds the lock for a moment: the change waits for it.
    [Fact]
    public void AHeldLock_IsWaitedFor()
    {
        var streams = new Mock<IFileStreamFactory>();
        streams.SetupSequence(factory => factory.New(It.IsAny<string>(), FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None, 1, FileOptions.DeleteOnClose))
            .Throws(SharingViolation())
            .Throws(SharingViolation())
            .Returns(() => _fileSystem.FileStream.New(SettingsPath + ".lock", FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None, 1, FileOptions.DeleteOnClose));

        new UserSettingsStore(FileSystemWith(ForwardingFile(), streams.Object), SettingsPath, LegacyPaths()) { LockRetryDelay = TimeSpan.FromMilliseconds(1) }
            .Update(ConfigurationSections.EDITOR, Changes(UserSettingsSchema.EDITOR_TYPE, "Vim"));

        Stored().ShouldBeJson("""{"editor":{"editorType":"Vim"}}""");
        streams.Verify(factory => factory.New(It.IsAny<string>(), It.IsAny<FileMode>(), It.IsAny<FileAccess>(), It.IsAny<FileShare>(), It.IsAny<int>(), It.IsAny<FileOptions>()), Times.Exactly(3));
    }

    [Fact]
    public void ALockThatStaysHeld_AsksToTryAgain()
    {
        var streams = new Mock<IFileStreamFactory>();
        streams.Setup(factory => factory.New(It.IsAny<string>(), It.IsAny<FileMode>(), It.IsAny<FileAccess>(), It.IsAny<FileShare>(), It.IsAny<int>(), It.IsAny<FileOptions>()))
            .Throws(SharingViolation());

        var exception = Should.Throw<UserSettingsException>(() => new UserSettingsStore(FileSystemWith(ForwardingFile(), streams.Object), SettingsPath, LegacyPaths()) { LockRetryDelay = TimeSpan.Zero }
            .Update(ConfigurationSections.EDITOR, Changes(UserSettingsSchema.EDITOR_TYPE, "Vim")));

        exception.Message.ShouldStartWith($"The settings file {SettingsPath} is being changed by another sform run, try it again: ");
        streams.Verify(factory => factory.New(It.IsAny<string>(), It.IsAny<FileMode>(), It.IsAny<FileAccess>(), It.IsAny<FileShare>(), It.IsAny<int>(), It.IsAny<FileOptions>()), Times.Exactly(50));
    }

    // A read-only file system (EROFS, 30) is no other run: it fails at once instead of after five seconds.
    [Fact]
    public void ALockThatCannotBeCreated_FailsAtOnce()
    {
        var streams = new Mock<IFileStreamFactory>();
        streams.Setup(factory => factory.New(It.IsAny<string>(), It.IsAny<FileMode>(), It.IsAny<FileAccess>(), It.IsAny<FileShare>(), It.IsAny<int>(), It.IsAny<FileOptions>()))
            .Throws(new IOException("Read-only file system", 30));

        var exception = Should.Throw<UserSettingsException>(() => new UserSettingsStore(FileSystemWith(ForwardingFile(), streams.Object), SettingsPath, LegacyPaths())
            .Update(ConfigurationSections.EDITOR, Changes(UserSettingsSchema.EDITOR_TYPE, "Vim")));

        exception.Message.ShouldBe($"The settings file {SettingsPath} cannot be written: Read-only file system");
        streams.Verify(factory => factory.New(It.IsAny<string>(), It.IsAny<FileMode>(), It.IsAny<FileAccess>(), It.IsAny<FileShare>(), It.IsAny<int>(), It.IsAny<FileOptions>()), Times.Once);
    }

    [Fact]
    public void TheDefaultLocation_IsTheFolderOfTheTokenStore()
    {
        var fileSystem = new FileSystem();

        UserSettingsLocation.GetFilePath(fileSystem).ShouldEndWith(fileSystem.Path.Combine(".42for.net", "sform.settings.json"));
        UserSettingsLocation.GetLegacyFilePaths(fileSystem)[ConfigurationSections.EDITOR].ShouldBe(fileSystem.Path.Combine(AppContext.BaseDirectory, "editor.config.json"));
        UserSettingsLocation.GetLegacyFilePaths(fileSystem)[ConfigurationSections.ACCESS].ShouldBe(fileSystem.Path.Combine(AppContext.BaseDirectory, "access.default.json"));
    }

    internal static IReadOnlyDictionary<string, string> LegacyPaths()
    {
        return new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            [ConfigurationSections.ACCESS] = LegacyAccessPath,
            [ConfigurationSections.EDITOR] = LegacyEditorPath,
        };
    }

    internal static IReadOnlyDictionary<string, JToken?> Changes(string property, string? value)
    {
        return new Dictionary<string, JToken?> { [property] = value is null ? null : new JValue(value) };
    }

    private static string P(string path) => MockUnixSupport.Path(path);

    private UserSettingsStore CreateStore()
    {
        return new UserSettingsStore(_fileSystem, SettingsPath, LegacyPaths());
    }

    private JObject Stored()
    {
        return JObject.Parse(_fileSystem.File.ReadAllText(SettingsPath));
    }

    private static IOException SharingViolation()
    {
        return new IOException("The process cannot access the file because it is being used by another process.", unchecked((int)0x80070020));
    }

    // A File that does what the mock file system does, for a test to replace one of its calls.
    private Mock<IFile> ForwardingFile()
    {
        var file = new Mock<IFile>();
        file.Setup(f => f.Exists(It.IsAny<string?>())).Returns<string?>(path => _fileSystem.File.Exists(path));
        file.Setup(f => f.ReadAllText(It.IsAny<string>())).Returns<string>(path => _fileSystem.File.ReadAllText(path));
        file.Setup(f => f.WriteAllText(It.IsAny<string>(), It.IsAny<string?>())).Callback<string, string?>((path, text) => _fileSystem.File.WriteAllText(path, text));
        file.Setup(f => f.Move(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<bool>())).Callback<string, string, bool>((from, to, overwrite) => _fileSystem.File.Move(from, to, overwrite));
        file.Setup(f => f.Delete(It.IsAny<string>())).Callback<string>(path => _fileSystem.File.Delete(path));
        return file;
    }

    private IFileSystem FileSystemWith(Mock<IFile> file, IFileStreamFactory? streams = null)
    {
        var fileSystem = new Mock<IFileSystem>();
        fileSystem.Setup(system => system.File).Returns(file.Object);
        fileSystem.Setup(system => system.Path).Returns(_fileSystem.Path);
        fileSystem.Setup(system => system.Directory).Returns(_fileSystem.Directory);
        fileSystem.Setup(system => system.FileStream).Returns(streams ?? _fileSystem.FileStream);
        fileSystem.Setup(system => system.FileInfo).Returns(_fileSystem.FileInfo);
        return fileSystem.Object;
    }

    // The mock file system with a File whose Delete fails, as in a read-only install folder.
    private IFileSystem UndeletableLegacyFiles()
    {
        var file = new Mock<IFile>();
        file.Setup(f => f.Exists(It.IsAny<string?>())).Returns<string?>(path => _fileSystem.File.Exists(path));
        file.Setup(f => f.ReadAllText(It.IsAny<string>())).Returns<string>(path => _fileSystem.File.ReadAllText(path));
        file.Setup(f => f.WriteAllText(It.IsAny<string>(), It.IsAny<string?>())).Callback<string, string?>((path, text) => _fileSystem.File.WriteAllText(path, text));
        file.Setup(f => f.Move(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<bool>())).Callback<string, string, bool>((from, to, overwrite) => _fileSystem.File.Move(from, to, overwrite));
        file.Setup(f => f.Delete(It.IsAny<string>())).Throws(new UnauthorizedAccessException("Read-only."));

        var fileSystem = new Mock<IFileSystem>();
        fileSystem.Setup(system => system.File).Returns(file.Object);
        fileSystem.Setup(system => system.Path).Returns(_fileSystem.Path);
        fileSystem.Setup(system => system.Directory).Returns(_fileSystem.Directory);
        fileSystem.Setup(system => system.FileStream).Returns(_fileSystem.FileStream);
        fileSystem.Setup(system => system.FileInfo).Returns(_fileSystem.FileInfo);
        return fileSystem.Object;
    }
}
