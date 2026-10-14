using _42.Platform.Storyteller.Transferring;

namespace _42.Platform.Storyteller.Backend.Core.UnitTests.Transferring;

public class ConfigurationFileNamesTests
{
    [Theory]
    [InlineData("rst.payments", null, "rst.payments.json")]
    [InlineData("rst.payments", 3UL, "rst.payments.3.json")]
    [InlineData("usg.billing.payments", null, "usg.billing.payments.json")]
    [InlineData("uxe.billing.payments.prod.nightly", 12UL, "uxe.billing.payments.prod.nightly.12.json")]
    public void GetFileName_JoinsTheKeyAndTheVersion(string key, ulong? version, string expected)
    {
        ConfigurationFileNames.GetFileName(key, version).ShouldBe(expected);
    }

    [Theory]
    [InlineData("rst.a:b", "rst.a%3Ab.json")]
    [InlineData("rst.50%", "rst.50%25.json")]
    [InlineData("sbt.<x>|y?*\"", "sbt.%3Cx%3E%7Cy%3F%2A%22.json")]
    [InlineData("rst.tab\there", "rst.tab%09here.json")]
    [InlineData("rst.café", "rst.café.json")]
    public void GetFileName_EncodesCharactersThatFileSystemsReject(string key, string expected)
    {
        ConfigurationFileNames.GetFileName(key).ShouldBe(expected);
    }

    [Theory]
    [InlineData("rst.a:b")]
    [InlineData("rst.50%")]
    [InlineData("sbt.<x>|y?*\"")]
    [InlineData("cnt.subject.prod")]
    [InlineData("exe.s.r.c")]
    [InlineData("uxe.s.r.c.u")]
    [InlineData("unt.r.u%25")]
    public void TryParse_ReversesGetFileName(string key)
    {
        ConfigurationFileNames.TryParse(ConfigurationFileNames.GetFileName(key), out var parsed, out var version).ShouldBeTrue();
        parsed!.ToString().ShouldBe(key);
        version.ShouldBeNull();

        ConfigurationFileNames.TryParse(ConfigurationFileNames.GetFileName(key, 7), out parsed, out version).ShouldBeTrue();
        parsed!.ToString().ShouldBe(key);
        version.ShouldBe(7UL);
    }

    [Fact]
    public void TryParse_TakesAnExtraNumericSegmentAsTheVersion()
    {
        // a responsibility has two segments, so "3" can only be a version
        ConfigurationFileNames.TryParse("rst.payments.3.json", out var key, out var version).ShouldBeTrue();
        key!.ToString().ShouldBe("rst.payments");
        version.ShouldBe(3UL);

        // a usage has three, so "3" is the responsibility's name
        ConfigurationFileNames.TryParse("usg.payments.3.json", out key, out version).ShouldBeTrue();
        key!.ToString().ShouldBe("usg.payments.3");
        version.ShouldBeNull();
    }

    [Theory]
    [InlineData("export.json")]
    [InlineData("README.md")]
    [InlineData("rst.json")]
    [InlineData("rst.payments.extra.json")]
    [InlineData("rst.payments.extra.more.json")]
    [InlineData("xyz.payments.json")]
    [InlineData("rst..json")]
    [InlineData("rst.payments.-1.json")]
    [InlineData("rst.payments.json.bak")]
    [InlineData("rst.bad%2.json")]
    [InlineData("rst.bad%ZZ.json")]
    [InlineData("rst.a%2Eb.json")]
    public void TryParse_RejectsOtherFiles(string fileName)
    {
        ConfigurationFileNames.TryParse(fileName, out var key, out var version).ShouldBeFalse();
        key.ShouldBeNull();
        version.ShouldBeNull();
    }

    [Fact]
    public void TryParse_AcceptsAnUpperCaseExtension()
    {
        ConfigurationFileNames.TryParse("rst.payments.JSON", out var key, out _).ShouldBeTrue();
        key!.ToString().ShouldBe("rst.payments");
    }

    [Theory]
    [InlineData(ConfigurationArchiveFormat.Folder, "configurations-acme-main-default-20261014-013012Z.zip")]
    [InlineData(ConfigurationArchiveFormat.Git, "configurations-acme-main-default-git-20261014-013012Z.zip")]
    public void GetArchiveFileName_HasTheUtcTime(ConfigurationArchiveFormat format, string expected)
    {
        var createdAt = new DateTimeOffset(2026, 10, 14, 3, 30, 12, TimeSpan.FromHours(2));
        ConfigurationFileNames.GetArchiveFileName("acme", "main", "default", format, createdAt).ShouldBe(expected);
    }
}
