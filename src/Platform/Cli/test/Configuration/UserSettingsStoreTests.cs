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

        Should.Throw<UserSettingsException>(() => new UserSettingsStore(fileSystem.Object, SettingsPath, LegacyPaths()).Load())
            .Message.ShouldBe($"The settings file {SettingsPath} cannot be read: The file is locked.");
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
