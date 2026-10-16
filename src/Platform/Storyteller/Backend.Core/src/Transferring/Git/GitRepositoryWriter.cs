using System.IO.Compression;
using System.Text;

namespace _42.Platform.Storyteller.Transferring.Git;

// Builds a git repository with a linear history on one branch and writes it into a zip with its working tree:
// .git (HEAD, config, the branch, a pack with its index, the index of the working tree) and the files of the last
// commit. A path with '/' lives in subtrees. A commit writes again only the trees on the paths it changes, and blobs
// and trees are stored as deltas against their previous versions.
public sealed class GitRepositoryWriter
{
    public const string Branch = "main";

    private const string FileMode = "100644";
    private const string TreeMode = "40000";

    private readonly SortedDictionary<string, TrackedFile> _files = new(GitPathComparer.Instance);
    private readonly TreeNode _root = new();
    private readonly GitPackWriter _pack = new();
    private byte[]? _head;

    public int CommitCount { get; private set; }

    // The files of the working tree: full paths in byte order, the order of .git/index.
    public IEnumerable<string> Paths => _files.Keys;

    // One commit: a null content removes the file, and a directory left empty goes with it. A commit without a
    // change is still written.
    public void Commit(IEnumerable<(string Path, byte[]? Content)> changes, GitSignature author, string message)
    {
        foreach (var (path, content) in changes)
        {
            var segments = SplitPath(path);

            if (content is null)
            {
                Remove(path, segments);
                continue;
            }

            var directory = Open(path, segments)[^1];
            var id = GitObjectId.Compute(GitObjectType.Blob, content);
            _files.TryGetValue(path, out var previous);
            _pack.Add(GitObjectType.Blob, id, content, previous?.Id, previous?.Content);

            var file = new TrackedFile(id, content, author.When);
            directory.Files[segments[^1]] = file;
            _files[path] = file;
        }

        var treeId = WriteTree(_root);

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

    // A relative path of a file: segments separated by '/', none of them empty, ".", ".." or ".git".
    private static string[] SplitPath(string path)
    {
        var segments = path.Split('/');
        var isValid = segments.All(segment => segment.Length > 0
            && segment is not ("." or "..")
            && !segment.Contains('\0')
            && !segment.Equals(".git", StringComparison.OrdinalIgnoreCase));

        if (!isValid)
        {
            throw new ArgumentException($"The path '{path}' is not a relative path of a file.", nameof(path));
        }

        return segments;
    }

    // The directories from the root to the one that holds the file, created when missing and marked as changed.
    private List<TreeNode> Open(string path, string[] segments)
    {
        var directories = new List<TreeNode>(segments.Length) { _root };
        _root.IsChanged = true;

        foreach (var name in segments[..^1])
        {
            var parent = directories[^1];

            if (parent.Files.ContainsKey(name))
            {
                throw new InvalidOperationException($"The path '{path}' runs through the file '{name}'.");
            }

            if (!parent.Directories.TryGetValue(name, out var directory))
            {
                directory = new TreeNode();
                parent.Directories.Add(name, directory);
            }

            directory.IsChanged = true;
            directories.Add(directory);
        }

        if (directories[^1].Directories.ContainsKey(segments[^1]))
        {
            throw new InvalidOperationException($"The path '{path}' is a directory.");
        }

        return directories;
    }

    private void Remove(string path, string[] segments)
    {
        if (!_files.Remove(path))
        {
            return;
        }

        var directories = Open(path, segments);
        directories[^1].Files.Remove(segments[^1]);

        // git has no empty directories: drop the ones the file leaves empty, from the deepest up
        for (var depth = directories.Count - 1; depth > 0 && directories[depth].IsEmpty; depth--)
        {
            directories[depth - 1].Directories.Remove(segments[depth - 1]);
        }
    }

    // Writes the tree of a changed directory, its changed subdirectories first, as a delta against the previous
    // version of the same tree; an unchanged directory keeps the id of its last tree.
    private byte[] WriteTree(TreeNode directory)
    {
        if (!directory.IsChanged && directory.TreeId is not null)
        {
            return directory.TreeId;
        }

        var entries = directory.Files
            .Select(file => (Name: file.Key, Mode: FileMode, file.Value.Id, IsTree: false))
            .Concat(directory.Directories.Select(subdirectory => (Name: subdirectory.Key, Mode: TreeMode, Id: WriteTree(subdirectory.Value), IsTree: true)))
            .OrderBy(entry => GitPathComparer.GetTreeEntryName(entry.Name, entry.IsTree), GitPathComparer.Instance)
            .ToList();

        using var tree = new MemoryStream();

        foreach (var entry in entries)
        {
            tree.Write(Encoding.ASCII.GetBytes(entry.Mode));
            tree.WriteByte((byte)' ');
            tree.Write(Encoding.UTF8.GetBytes(entry.Name));
            tree.WriteByte(0);
            tree.Write(entry.Id);
        }

        var content = tree.ToArray();
        var id = GitObjectId.Compute(GitObjectType.Tree, content);
        _pack.Add(GitObjectType.Tree, id, content, directory.TreeId, directory.TreeContent);

        directory.TreeId = id;
        directory.TreeContent = content;
        directory.IsChanged = false;
        return id;
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

    // A directory of the working tree and the tree object the last commit wrote for it.
    private sealed class TreeNode
    {
        public Dictionary<string, TrackedFile> Files { get; } = new(StringComparer.Ordinal);

        public Dictionary<string, TreeNode> Directories { get; } = new(StringComparer.Ordinal);

        public bool IsChanged { get; set; } = true;

        public bool IsEmpty => Files.Count == 0 && Directories.Count == 0;

        public byte[]? TreeId { get; set; }

        public byte[]? TreeContent { get; set; }
    }
}
