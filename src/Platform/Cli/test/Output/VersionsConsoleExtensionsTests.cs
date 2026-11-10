using System;
using _42.Platform.Cli.Output;
using _42.Platform.Cli.UnitTests.Commands;
using _42.Platform.Storyteller.Sdk;
using Shouldly;
using Xunit;

namespace _42.Platform.Cli.UnitTests.Output;

// The table of config, template and schema … versions: the newest first, and the column Restored from (424-221) only
// when a listed version was stored by a restore of a version.
public class VersionsConsoleExtensionsTests
{
    private static readonly DateTimeOffset Created = new(2026, 11, 10, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public void Versions_WithoutARestore_HaveNoRestoredFromColumn()
    {
        var console = new RecordingConsole();

        console.Object.WriteVersions([Version(1, expires: true), Version(2, expires: false)]);

        var table = console.Tables.ShouldHaveSingleItem();
        table.Headers.ShouldBe(["Version", "Author", "Created", "Expires"]);
        table.Rows.Count.ShouldBe(2);
        table.Rows[0][0].ShouldBe("2");
        table.Rows[0][3].ShouldBe("—");
        table.Rows[1][0].ShouldBe("1");
        table.Rows.ShouldAllBe(row => row.Count == 4);
    }

    [Fact]
    public void Versions_WithARestore_NameTheVersionEachCopied()
    {
        var console = new RecordingConsole();
        var restored = Version(4, expires: false);
        restored.RestoredFrom = 1;

        console.Object.WriteVersions([Version(1, expires: true), Version(2, expires: true), Version(3, expires: true), restored]);

        var table = console.Tables.ShouldHaveSingleItem();
        table.Headers.ShouldBe(["Version", "Author", "Created", "Expires", "Restored from"]);
        table.Rows.Count.ShouldBe(4);
        table.Rows[0][0].ShouldBe("4");
        table.Rows[0][1].ShouldBe("Ales Kobr");
        table.Rows[0][3].ShouldBe("—");
        table.Rows[0][4].ShouldBe("v1");
        table.Rows[1][0].ShouldBe("3");
        table.Rows[1][4].ShouldBe("—");
        table.Rows[3][4].ShouldBe("—");
    }

    private static ConfigurationVersion Version(int version, bool expires)
    {
        return new ConfigurationVersion
        {
            Version = version,
            Modified = new AuditStamp
            {
                At = Created.AddMinutes(version),
                By = new AuditActor { Kind = AuditActorKind.Account, Id = "user_1", Name = "Ales Kobr" },
            },
            ExpirationTime = expires ? Created.AddDays(365) : DateTimeOffset.MaxValue,
        };
    }
}
