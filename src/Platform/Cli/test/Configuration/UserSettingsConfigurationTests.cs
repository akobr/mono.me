using System;
using System.Collections.Generic;
using System.IO.Abstractions.TestingHelpers;
using _42.Platform.Cli.Configuration;
using Microsoft.Extensions.Configuration;
using Shouldly;
using Xunit;

namespace _42.Platform.Cli.UnitTests.Configuration;

// What the options bind: the settings file over app.config.json, the legacy files while the settings file lacks their
// section, and never a value that would fail the binding.
public class UserSettingsConfigurationTests
{
    private static readonly string SettingsPath = UserSettingsStoreTests.SettingsPath;

    private readonly MockFileSystem _fileSystem = new(new Dictionary<string, MockFileData>(), MockUnixSupport.Path(@"C:\work"));
    private readonly List<string> _warnings = [];

    [Fact]
    public void TheSettingsFile_BindsTheEditorAndTheDefaultProject()
    {
        _fileSystem.AddFile(SettingsPath, new MockFileData("""
            {
              "editor": { "editorType": "Custom", "customCommand": "code --wait" },
              "access": { "organizationName": "contoso", "projectName": "payments", "viewName": "preview" }
            }
            """));

        var configuration = Build();

        var editor = Bind<EditorOptions>(configuration, ConfigurationSections.EDITOR);
        editor.EditorType.ShouldBe(EditorType.Custom);
        editor.CustomCommand.ShouldBe("code --wait");
        var access = Bind<AccessDefaultOptions>(configuration, ConfigurationSections.ACCESS);
        access.OrganizationName.ShouldBe("contoso");
        access.ProjectName.ShouldBe("payments");
        access.ViewName.ShouldBe("preview");
        _warnings.ShouldBeEmpty();
    }

    [Fact]
    public void TheLegacyFiles_AreStillHonoured()
    {
        _fileSystem.AddFile(UserSettingsStoreTests.LegacyEditorPath, new MockFileData(UserSettingsStoreTests.LegacyEditorJson));
        _fileSystem.AddFile(UserSettingsStoreTests.LegacyAccessPath, new MockFileData(UserSettingsStoreTests.LegacyAccessJson));

        var configuration = Build();

        Bind<EditorOptions>(configuration, ConfigurationSections.EDITOR).EditorType.ShouldBe(EditorType.Neovim);
        var access = Bind<AccessDefaultOptions>(configuration, ConfigurationSections.ACCESS);
        access.OrganizationName.ShouldBe("contoso");
        access.ProjectName.ShouldBe("payments");
        access.ViewName.ShouldBeNull();
    }

    [Fact]
    public void TheSettingsFile_WinsOverALegacyFile_SectionBySection()
    {
        _fileSystem.AddFile(SettingsPath, new MockFileData("""{"editor":{"editorType":"Vim"}}"""));
        _fileSystem.AddFile(UserSettingsStoreTests.LegacyEditorPath, new MockFileData("""{"editor":{"EditorType":3,"CustomCommand":"nano"}}"""));
        _fileSystem.AddFile(UserSettingsStoreTests.LegacyAccessPath, new MockFileData(UserSettingsStoreTests.LegacyAccessJson));

        var configuration = Build();

        var editor = Bind<EditorOptions>(configuration, ConfigurationSections.EDITOR);
        editor.EditorType.ShouldBe(EditorType.Vim);
        editor.CustomCommand.ShouldBeNull();
        Bind<AccessDefaultOptions>(configuration, ConfigurationSections.ACCESS).ProjectName.ShouldBe("payments");
    }

    // An unset editor leaves "editor": {} while the old file stays: the old editor must not come back.
    [Fact]
    public void AnEmptySection_HidesTheLegacyFile()
    {
        _fileSystem.AddFile(SettingsPath, new MockFileData("""{"editor":{}}"""));
        _fileSystem.AddFile(UserSettingsStoreTests.LegacyEditorPath, new MockFileData(UserSettingsStoreTests.LegacyEditorJson));

        Bind<EditorOptions>(Build(), ConfigurationSections.EDITOR).IsConfigured.ShouldBeFalse();
    }

    [Fact]
    public void TheSettingsFile_WinsOverAppConfig()
    {
        _fileSystem.AddFile(SettingsPath, new MockFileData("""{"access":{"viewName":"preview"}}"""));

        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { ["access:viewName"] = "default", ["general:baseUrl"] = "https://api" })
            .Add(new UserSettingsConfigurationSource(CreateStore, _warnings.Add))
            .Build();

        configuration["access:ViewName"].ShouldBe("preview");
        configuration["general:baseUrl"].ShouldBe("https://api");
    }

    // A value written by hand that the binder cannot convert would fail every edit command; it is left out instead.
    [Theory]
    [InlineData("\"notepad\"")]
    [InlineData("7")]
    [InlineData("\"VsCode, Vim\"")]
    [InlineData("{}")]
    public void AnInvalidEditorType_IsIgnored(string value)
    {
        _fileSystem.AddFile(SettingsPath, new MockFileData($$$"""{"editor":{"editorType":{{{value}}},"customCommand":"nano"}}"""));

        var editor = Bind<EditorOptions>(Build(), ConfigurationSections.EDITOR);

        editor.EditorType.ShouldBeNull();
        editor.CustomCommand.ShouldBe("nano");
    }

    [Theory]
    [InlineData("\"vim\"", EditorType.Vim)]
    [InlineData("\"VSCODE\"", EditorType.VsCode)]
    [InlineData("3", EditorType.Custom)]
    [InlineData("\"1\"", EditorType.Neovim)]
    public void AnEditorType_IsReadByNameOrNumber(string value, EditorType expected)
    {
        _fileSystem.AddFile(SettingsPath, new MockFileData($$$"""{"editor":{"editorType":{{{value}}}}}"""));

        Bind<EditorOptions>(Build(), ConfigurationSections.EDITOR).EditorType.ShouldBe(expected);
    }

    [Fact]
    public void OnlyTheKnownSections_AreLoaded()
    {
        _fileSystem.AddFile(SettingsPath, new MockFileData("""{"general":{"baseUrl":"https://elsewhere"},"editor":{"editorType":"Vim","extra":"x"}}"""));

        var configuration = Build();

        configuration["general:baseUrl"].ShouldBeNull();
        configuration["editor:extra"].ShouldBeNull();
        configuration["editor:editorType"].ShouldBe("Vim");
    }

    // Not the legacy files either: a settings file replaced them, so their values may be stale.
    [Fact]
    public void ADamagedSettingsFile_IsReported_AndGivesNoSettings()
    {
        _fileSystem.AddFile(SettingsPath, new MockFileData("{"));
        _fileSystem.AddFile(UserSettingsStoreTests.LegacyAccessPath, new MockFileData(UserSettingsStoreTests.LegacyAccessJson));

        var configuration = Build();

        Bind<AccessDefaultOptions>(configuration, ConfigurationSections.ACCESS).ProjectName.ShouldBeNull();
        _warnings.ShouldHaveSingleItem().ShouldStartWith($"The settings file {SettingsPath} is damaged: ");
        _warnings[0].ShouldEndWith("sform uses no editor and no default project from it until it is fixed (sform account settings --path).");
    }

    // E.g. no HOME on Linux: every command would fail to start, also in a pipeline that never uses the settings.
    [Fact]
    public void AStoreThatCannotBeCreated_IsReported_AndStopsNothing()
    {
        var configuration = new ConfigurationBuilder()
            .Add(new UserSettingsConfigurationSource(() => throw new InvalidOperationException("The folder of the user is unknown; set HOME."), _warnings.Add))
            .Build();

        configuration.GetSection(ConfigurationSections.EDITOR).Exists().ShouldBeFalse();
        _warnings.ShouldBe(["The settings of sform cannot be read: The folder of the user is unknown; set HOME."]);
    }

    [Fact]
    public void NoFiles_BindNothing()
    {
        var configuration = Build();

        Bind<EditorOptions>(configuration, ConfigurationSections.EDITOR).IsConfigured.ShouldBeFalse();
        Bind<AccessDefaultOptions>(configuration, ConfigurationSections.ACCESS).ProjectName.ShouldBeNull();
    }

    private static T Bind<T>(IConfiguration configuration, string section)
        where T : new()
    {
        var options = new T();
        configuration.GetSection(section).Bind(options);
        return options;
    }

    private UserSettingsStore CreateStore()
    {
        return new UserSettingsStore(_fileSystem, SettingsPath, UserSettingsStoreTests.LegacyPaths());
    }

    private IConfiguration Build()
    {
        return new ConfigurationBuilder().Add(new UserSettingsConfigurationSource(CreateStore, _warnings.Add)).Build();
    }
}
