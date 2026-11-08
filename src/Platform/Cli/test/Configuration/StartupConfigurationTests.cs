using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Abstractions;
using System.Linq;
using _42.Platform.Cli.Configuration;
using Microsoft.Extensions.Configuration;
using Shouldly;
using Xunit;

namespace _42.Platform.Cli.UnitTests.Configuration;

// The configuration files on disk: app.config.json next to the assembly, the files sform writes in SFORM_HOME.
public sealed class StartupConfigurationTests : IDisposable
{
    private readonly string _home = Path.Combine(Path.GetTempPath(), "sform-home-" + Guid.NewGuid().ToString("N"));

    [Fact]
    public void WithHome_TheDefaultProjectAndTheEditorAreReadFromIt_AndTheApplicationSettingsFromTheAssemblyFolder()
    {
        Directory.CreateDirectory(_home);
        File.WriteAllText(Path.Combine(_home, "access.default.json"), """{"access":{"organizationName":"acme","projectName":"billing","viewName":"preview"}}""");
        File.WriteAllText(Path.Combine(_home, "editor.config.json"), """{"editor":{"editorType":"Custom","customCommand":"fake-editor"}}""");

        var configuration = Build();

        configuration["access:organizationName"].ShouldBe("acme");
        configuration["access:projectName"].ShouldBe("billing");
        configuration["access:viewName"].ShouldBe("preview");
        configuration["editor:customCommand"].ShouldBe("fake-editor");
        configuration["general:userAgent"].ShouldBe("42.sform.cli");
    }

    [Fact]
    public void WithAHomeThatDoesNotExistYet_NothingIsRead()
    {
        var configuration = Build();

        configuration["access:projectName"].ShouldBeNull();
        configuration["editor:editorType"].ShouldBeNull();
        Directory.Exists(_home).ShouldBeFalse();
    }

    // SFORM_<section>__<key> wins over the sources before it (e.g. SFORM_general__baseUrl over app.config.json). A section
    // of its own keeps the variable away from the tests that run at the same time; the settings store reads only the known
    // sections, so the section comes from a source of the test in place of a file.
    [Fact]
    public void AnSformVariable_WinsOverTheFiles()
    {
        var section = "sformtest" + Guid.NewGuid().ToString("N");
        var variable = $"SFORM_{section}__baseUrl";
        Environment.SetEnvironmentVariable(variable, "http://localhost:7071/api");

        try
        {
            var configuration = Build(withVariables: true, (section + ":baseUrl", "https://from.file/api"), (section + ":other", "kept"));

            configuration[$"{section}:baseUrl"].ShouldBe("http://localhost:7071/api");
            configuration[$"{section}:other"].ShouldBe("kept");
        }
        finally
        {
            Environment.SetEnvironmentVariable(variable, null);
        }
    }

    public void Dispose()
    {
        if (Directory.Exists(_home))
        {
            Directory.Delete(_home, recursive: true);
        }
    }

    private IConfigurationRoot Build(bool withVariables = false, params (string Key, string Value)[] settings)
    {
        var paths = CliPaths.Create(new FileSystem(), _home, AppContext.BaseDirectory, () => throw new InvalidOperationException("The user profile was read."));
        var builder = new ConfigurationBuilder().SetBasePath(AppContext.BaseDirectory);
        Startup.AddConfigurationFiles(builder, paths);
        builder.AddInMemoryCollection(settings.Select(setting => new KeyValuePair<string, string?>(setting.Key, setting.Value)));

        if (withVariables)
        {
            Startup.AddEnvironmentVariables(builder);
        }

        return builder.Build();
    }
}
