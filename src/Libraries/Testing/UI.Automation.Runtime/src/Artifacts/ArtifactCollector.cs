using System.Collections.Concurrent;
using System.IO.Abstractions;
using System.Text;
using _42.Testing.UI.Automation.Runtime.Data;

namespace _42.Testing.UI.Automation.Runtime.Artifacts;

// The files of one attempt of one test, in <run directory>/<Class>/<Test>[/attempt-n]/: trace.zip, failure.png,
// console.log, video.webm. The class is its name below the assembly's namespace (Tests.Access.SignInTests for
// _42.Platform.UI.Admin.AutomationTests.Tests.Access.SignInTests), so classes of the same name in two areas do not
// share a folder; a name that had to be sanitized or shortened adds a short hash of the full name. The test is the
// method; a data-driven case or a repetition, whose id is more than <Assembly>/<Class>/<Method>, is its display name
// and a short hash of its id. Every test instance and attempt has a folder of its own, so parallel tests never write
// over each other's files. The directory is created on first use, so a passing test without artifacts leaves none.
public sealed class ArtifactCollector
{
    private const int MaxSegmentLength = 80;

    private static readonly UTF8Encoding Utf8 = new(encoderShouldEmitUTF8Identifier: false);

    private readonly IFileSystem fileSystem;
    private readonly ConcurrentQueue<TestArtifact> artifacts = new();

    public ArtifactCollector(IFileSystem fileSystem, string runDirectory, TestIdentity test, int attempt)
    {
        ArgumentNullException.ThrowIfNull(fileSystem);
        ArgumentException.ThrowIfNullOrWhiteSpace(runDirectory);
        ArgumentNullException.ThrowIfNull(test);

        this.fileSystem = fileSystem;
        Directory = GetDirectory(fileSystem, runDirectory, test, attempt);
    }

    public string Directory { get; }

    public IReadOnlyList<TestArtifact> Artifacts => artifacts.ToArray();

    public static string GetDirectory(IFileSystem fileSystem, string runDirectory, TestIdentity test, int attempt)
    {
        ArgumentNullException.ThrowIfNull(fileSystem);
        ArgumentNullException.ThrowIfNull(test);

        var directory = fileSystem.Path.Combine(runDirectory, ClassSegment(test), TestSegment(test));
        return attempt > 1 ? fileSystem.Path.Combine(directory, $"attempt-{attempt}") : directory;
    }

    // The path of a file of the test; creates the directory.
    public string GetPath(string fileName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(fileName);

        fileSystem.Directory.CreateDirectory(Directory);
        return fileSystem.Path.Combine(Directory, fileName);
    }

    // Lists a file the test or Playwright wrote; null when it does not exist.
    public TestArtifact? Add(string path, ArtifactKind kind, string? name = null, string? mimeType = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);

        var file = fileSystem.FileInfo.New(path);
        if (!file.Exists)
        {
            return null;
        }

        var artifact = new TestArtifact(name ?? file.Name, file.FullName, mimeType ?? MimeTypes.Of(file.Name), kind, file.Length);
        artifacts.Enqueue(artifact);
        return artifact;
    }

    public async Task<TestArtifact> WriteTextAsync(string fileName, string content, ArtifactKind kind, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(content);

        var path = GetPath(fileName);
        await fileSystem.File.WriteAllTextAsync(path, content, Utf8, cancellationToken);
        return Add(path, kind) ?? throw new IOException($"{path} was not written.");
    }

    // Letters, digits, '.', '-' and '_'; anything else becomes '_', runs of it one, at most 80 characters.
    internal static string Sanitize(string value, int maxLength = MaxSegmentLength)
    {
        var builder = new StringBuilder(value.Length);
        foreach (var character in value)
        {
            if (char.IsAsciiLetterOrDigit(character) || character is '.' or '-' or '_')
            {
                builder.Append(character);
            }
            else if (builder.Length > 0 && builder[^1] != '_')
            {
                builder.Append('_');
            }
        }

        var sanitized = builder.ToString().Trim('_', '.');
        if (sanitized.Length > maxLength)
        {
            sanitized = sanitized[..maxLength].TrimEnd('_', '.');
        }

        return sanitized.Length == 0 ? "_" : sanitized;
    }

    private static string ClassSegment(TestIdentity test)
    {
        var name = test.ClassName;
        foreach (var prefix in new[] { $"{test.Assembly}.", $"_{test.Assembly}." })
        {
            if (name.Length > prefix.Length && name.StartsWith(prefix, StringComparison.Ordinal))
            {
                name = name[prefix.Length..];
                break;
            }
        }

        var segment = Sanitize(name);
        return string.Equals(segment, name, StringComparison.Ordinal) ? segment : WithHash(name, test.ClassName);
    }

    private static string TestSegment(TestIdentity test)
    {
        var single = string.Equals(test.Id, $"{test.Assembly}/{test.ClassName}/{test.MethodName}", StringComparison.Ordinal)
            && (string.IsNullOrEmpty(test.DisplayName) || string.Equals(test.DisplayName, test.MethodName, StringComparison.Ordinal));

        return single
            ? Sanitize(test.MethodName)
            : WithHash(string.IsNullOrEmpty(test.DisplayName) ? test.MethodName : test.DisplayName, test.Id);
    }

    // The sanitized name, shortened to leave room for '-' and the 8 hex digits of the hash.
    private static string WithHash(string name, string hashed)
    {
        return $"{Sanitize(name, MaxSegmentLength - 9)}-{StableHash.Fnv1a32(hashed):x8}";
    }
}
