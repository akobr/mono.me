using System.IO.Abstractions.TestingHelpers;
using _42.Testing.UI.Automation.Runtime.Artifacts;
using _42.Testing.UI.Automation.Runtime.UnitTests.Fakes;

namespace _42.Testing.UI.Automation.Runtime.UnitTests.Artifacts;

public sealed class ArtifactCollectorTests
{
    private static readonly string RunDirectory = MockUnixSupport.Path(@"C:\results\20261031-142500-abcd");

    [Fact]
    public void Directory_IsTheClassAndTheMethod_WithTheAttemptOfARetry()
    {
        var files = new MockFileSystem();
        var test = TestData.Identity("SignIn_ShowsOrganizations");

        new ArtifactCollector(files, RunDirectory, test, 1).Directory.ShouldBe(files.Path.Combine(RunDirectory, "Sample.Tests.SampleTests", "SignIn_ShowsOrganizations"));
        new ArtifactCollector(files, RunDirectory, test, 2).Directory.ShouldBe(files.Path.Combine(RunDirectory, "Sample.Tests.SampleTests", "SignIn_ShowsOrganizations", "attempt-2"));
        files.Directory.Exists(RunDirectory).ShouldBeFalse();
    }

    [Fact]
    public void Directory_DropsTheAssemblyNamespace_AndKeepsClassesOfTheSameNameApart()
    {
        var files = new MockFileSystem();
        var annotations = Identity("_42.Sample.Tests.Annotations.CrudTests", "Create");
        var keyValues = Identity("_42.Sample.Tests.KeyValues.CrudTests", "Create");

        var first = new ArtifactCollector(files, RunDirectory, annotations, 1).Directory;
        var second = new ArtifactCollector(files, RunDirectory, keyValues, 1).Directory;

        first.ShouldBe(files.Path.Combine(RunDirectory, "Tests.Annotations.CrudTests", "Create"));
        second.ShouldBe(files.Path.Combine(RunDirectory, "Tests.KeyValues.CrudTests", "Create"));
    }

    [Fact]
    public void Directory_OfANestedClass_AddsAHashOfTheFullName()
    {
        var files = new MockFileSystem();
        var one = Identity("_42.Sample.Outer+Inner", "Test");
        var other = Identity("_42.Sample.Outer_Inner", "Test");

        var first = new ArtifactCollector(files, RunDirectory, one, 1).Directory;
        var second = new ArtifactCollector(files, RunDirectory, other, 1).Directory;

        files.Path.GetFileName(files.Path.GetDirectoryName(first)!).ShouldMatch("^Outer_Inner-[0-9a-f]{8}$");
        first.ShouldNotBe(second);
    }

    [Fact]
    public void Directory_OfADataDrivenCaseOrARepetition_AddsItsDisplayNameAndAHashOfItsId()
    {
        var files = new MockFileSystem();
        var one = Identity("_42.Sample.DataTests", "Data", "Data(1, one)", "/Data(1, one)");
        var other = Identity("_42.Sample.DataTests", "Data", "Data(1,one)", "/Data(1,one)");
        var repeated = Identity("_42.Sample.DataTests", "Data", "Data", "#0a1b2c3d");
        var repeatedAgain = Identity("_42.Sample.DataTests", "Data", "Data", "#4e5f6a7b");

        var directories = new[] { one, other, repeated, repeatedAgain }.Select(test => new ArtifactCollector(files, RunDirectory, test, 1).Directory).ToArray();

        files.Path.GetFileName(directories[0]).ShouldMatch("^Data_1_one-[0-9a-f]{8}$");
        files.Path.GetFileName(directories[2]).ShouldMatch("^Data-[0-9a-f]{8}$");
        directories.Distinct().Count().ShouldBe(4);
    }

    [Fact]
    public async Task WriteTextAsync_AndAdd_ListTheArtifacts()
    {
        var files = new MockFileSystem();
        var collector = new ArtifactCollector(files, RunDirectory, TestData.Identity("Test"), 1);

        var log = await collector.WriteTextAsync("console.log", "12:00:00.000 [error] boom\n", ArtifactKind.ConsoleLog);
        var screenshot = collector.GetPath("failure.png");
        files.AddFile(screenshot, new MockFileData(new byte[] { 1, 2, 3 }));

        collector.Add(screenshot, ArtifactKind.Screenshot).ShouldNotBeNull().SizeBytes.ShouldBe(3);
        collector.Add(collector.GetPath("trace.zip"), ArtifactKind.Trace).ShouldBeNull();

        log.MimeType.ShouldBe("text/plain");
        log.SizeBytes.ShouldBe(26);
        files.File.ReadAllText(log.Path).ShouldBe("12:00:00.000 [error] boom\n");
        collector.Artifacts.Select(artifact => (artifact.Name, artifact.Kind, artifact.MimeType)).ShouldBe(
        [
            ("console.log", ArtifactKind.ConsoleLog, "text/plain"),
            ("failure.png", ArtifactKind.Screenshot, "image/png"),
        ]);
    }

    [Theory]
    [InlineData("Annotations_Create", "Annotations_Create")]
    [InlineData("Data(1, \"one\")", "Data_1_one")]
    [InlineData("../../etc", "etc")]
    [InlineData("???", "_")]
    public void Sanitize_KeepsPortableCharacters(string value, string expected)
    {
        ArtifactCollector.Sanitize(value).ShouldBe(expected);
    }

    private static TestIdentity Identity(string className, string method, string? displayName = null, string idSuffix = "")
    {
        return new TestIdentity($"42.Sample/{className}/{method}{idSuffix}", displayName ?? method, className, method, "42.Sample", [], []);
    }
}
