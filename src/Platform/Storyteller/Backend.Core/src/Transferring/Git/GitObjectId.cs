using System.Globalization;
using System.Security.Cryptography;
using System.Text;

namespace _42.Platform.Storyteller.Transferring.Git;

public static class GitObjectId
{
    public const int Length = 20;

    // SHA-1 over "{type} {length}\0{content}"; git uses it as a name, not for security.
    public static byte[] Compute(GitObjectType type, ReadOnlySpan<byte> content)
    {
        var header = Encoding.ASCII.GetBytes($"{GetTypeName(type)} {content.Length.ToString(CultureInfo.InvariantCulture)}\0");
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA1);
        hash.AppendData(header);
        hash.AppendData(content);
        return hash.GetHashAndReset();
    }

    public static string ToHex(ReadOnlySpan<byte> id)
    {
        return Convert.ToHexStringLower(id);
    }

    public static string GetTypeName(GitObjectType type)
    {
        return type switch
        {
            GitObjectType.Commit => "commit",
            GitObjectType.Tree => "tree",
            GitObjectType.Blob => "blob",
            _ => throw new ArgumentOutOfRangeException(nameof(type), type, "Only commits, trees and blobs have names."),
        };
    }
}
