using System.IO.Compression;
using System.Text;

namespace _42.Platform.Storyteller.Transferring.Git;

// Builds a git repository with a linear history on one branch and writes it into a zip with its working tree:
// .git (HEAD, config, the branch, a pack with its index, the index of the working tree) and the files of the last
// commit. Files are flat; blobs and trees are stored as deltas against their previous versions.
public sealed class GitRepositoryWriter
{
    public const string Branch = "main";

    private readonly SortedDictionary<string, TrackedFile> _files = new(GitPathComparer.Instance);
    private readonly GitPackWriter _pack = new();
    private byte[]? _head;
    private byte[]? _treeId;
    private byte[]? _treeContent;

    public int CommitCount { get; private set; }

    public IEnumerable<string> Paths => _files.Keys;

    // One commit: a null content removes the file. A commit without a change is still written.
    public void Commit(IEnumerable<(string Path, byte[]? Content)> changes, GitSignature author, string message)
    {
        foreach (var (path, content) in changes)
        {
            if (content is null)
            {
                _files.Remove(path);
                continue;
            }

            var id = GitObjectId.Compute(GitObjectType.Blob, content);
            _files.TryGetValue(path, out var previous);
            _pack.Add(GitObjectType.Blob, id, content, previous?.Id, previous?.Content);
            _files[path] = new TrackedFile(id, content, author.When);
        }

        var treeContent = CreateTree();
        var treeId = GitObjectId.Compute(GitObjectType.Tree, treeContent);
        _pack.Add(GitObjectType.Tree, treeId, treeContent, _treeId, _treeContent);
        _treeId = treeId;
        _treeContent = treeContent;

        var commit = new StringBuilder();
        commit.Append("tree ").Append(GitObjectId.ToHex(treeId)).Append('\n');

        if (_head is not null)
        {
            commit.Append("parent ").Append(GitObjectId.ToHex(_head)).Append('\n');
        }

        commit.Append("author ").Append(author.Format()).Append('\n');
        commit.Append("committer ").Append(author.Format()).Append('\n');
        commit.Append('\n').Append(message.TrimEnd('\n')).Append('\n');

        var commitContent = Encoding.UTF8.GetBytes(commit.ToString());
        var commitId = GitObjectId.Compute(GitObjectType.Commit, commitContent);
        _pack.Add(GitObjectType.Commit, commitId, commitContent);
        _head = commitId;
        CommitCount++;
    }

    public void WriteTo(ZipArchive archive, DateTimeOffset now)
    {
        foreach (var (path, file) in _files)
        {
            WriteEntry(archive, path, file.Content, file.ModifiedAt, CompressionLevel.Optimal);
        }

        WriteDirectory(archive, ".git/", now);
        WriteEntry(archive, ".git/HEAD", Encoding.ASCII.GetBytes($"ref: refs/heads/{Branch}\n"), now, CompressionLevel.Optimal);
        WriteEntry(
            archive,
            ".git/config",
            Encoding.ASCII.GetBytes("[core]\n\trepositoryformatversion = 0\n\tfilemode = false\n\tbare = false\n\tlogallrefupdates = true\n"),
            now,
            CompressionLevel.Optimal);
        WriteDirectory(archive, ".git/refs/", now);
        WriteDirectory(archive, ".git/refs/heads/", now);
        WriteDirectory(archive, ".git/refs/tags/", now);
        WriteDirectory(archive, ".git/objects/", now);
        WriteDirectory(archive, ".git/objects/info/", now);
        WriteDirectory(archive, ".git/objects/pack/", now);

        if (_head is null)
        {
            // an unborn branch: no commit, no pack, no index
            return;
        }

        WriteEntry(archive, $".git/refs/heads/{Branch}", Encoding.ASCII.GetBytes($"{GitObjectId.ToHex(_head)}\n"), now, CompressionLevel.Optimal);

        var pack = _pack.Complete();
        WriteEntry(archive, $".git/objects/pack/{pack.Name}.pack", pack.Pack, now, CompressionLevel.NoCompression);
        WriteEntry(archive, $".git/objects/pack/{pack.Name}.idx", pack.Index, now, CompressionLevel.Optimal);

        var index = GitIndexWriter.Write(_files.Select(item => new GitIndexEntry(item.Key, item.Value.Id, item.Value.Content.Length, item.Value.ModifiedAt)));
        WriteEntry(archive, ".git/index", index, now, CompressionLevel.Optimal);
    }

    // Zip times cannot be before 1980.
    internal static DateTimeOffset ZipTime(DateTimeOffset value)
    {
        var minimum = new DateTimeOffset(1980, 1, 1, 0, 0, 0, TimeSpan.Zero);
        return value < minimum ? minimum : value;
    }

    private byte[] CreateTree()
    {
        using var tree = new MemoryStream();

        foreach (var (path, file) in _files)
        {
            tree.Write("100644 "u8);
            tree.Write(Encoding.UTF8.GetBytes(path));
            tree.WriteByte(0);
            tree.Write(file.Id);
        }

        return tree.ToArray();
    }

    private static void WriteEntry(ZipArchive archive, string name, byte[] content, DateTimeOffset modifiedAt, CompressionLevel compression)
    {
        var entry = archive.CreateEntry(name, compression);
        entry.LastWriteTime = ZipTime(modifiedAt);
        using var stream = entry.Open();
        stream.Write(content);
    }

    private static void WriteDirectory(ZipArchive archive, string name, DateTimeOffset modifiedAt)
    {
        archive.CreateEntry(name).LastWriteTime = ZipTime(modifiedAt);
    }

    private sealed record class TrackedFile(byte[] Id, byte[] Content, DateTimeOffset ModifiedAt);
}
