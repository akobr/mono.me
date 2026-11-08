using System.Security.Cryptography;
using System.Text.RegularExpressions;
using _42.Platform.Cli.AutomationTests.Running;

namespace _42.Platform.Cli.AutomationTests.Tests.KeyValues;

// Helpers of the Pairs, Secrets, List/Delete/Bulk and Context tests of sform story kv:
//   - the rows of the tables sform prints (kv, kv versions, kv protect|restore|purge): one line per row, its first cell the
//     key, the cells separated by spaces (the table renders at COLUMNS, 1000, so nothing wraps);
//   - the continuation token of "More keys follow; next page with -k <token>";
//   - the id of "Recipient '<id>' (<name>) added.";
//   - RSA-3072 key pairs for the recipients of client-encrypted (hyok) values;
//   - a wait for the API's cached key/value policy: an instance of the API caches a project's policy for a minute
//     (CosmosKeyValuePolicyStore), and the writes and the reveals check that copy, so a policy change can reach a call on
//     another instance up to a minute later.
internal static partial class KeyValuesSupportA
{
    // A minute of the API's policy cache, and some slack.
    public static readonly TimeSpan PolicyCacheWait = TimeSpan.FromSeconds(90);

    private static readonly TimeSpan PollInterval = TimeSpan.FromSeconds(5);

    // The row of the key in a table sform printed: the line whose first cell is the key; null when it lists none.
    public static string? RowOf(SformResult result, string key)
    {
        ArgumentNullException.ThrowIfNull(result);
        ArgumentException.ThrowIfNullOrWhiteSpace(key);
        return result.Lines.FirstOrDefault(line => string.Equals(FirstCell(line), key, StringComparison.Ordinal));
    }

    public static bool Lists(SformResult result, string key)
    {
        return RowOf(result, key) is not null;
    }

    // The keys of the table's rows that start with the root (a generated key the test put its keys under), in their order.
    public static IReadOnlyList<string> ListedKeys(SformResult result, string root)
    {
        ArgumentNullException.ThrowIfNull(result);
        ArgumentException.ThrowIfNullOrWhiteSpace(root);
        return result.Lines
            .Select(FirstCell)
            .Where(cell => cell is not null && cell.StartsWith(root, StringComparison.OrdinalIgnoreCase))
            .Select(cell => cell!)
            .ToList();
    }

    // The token of "More keys follow; next page with -k <token>" (KeyValueConsoleExtensions.WriteKeyValues); null when
    // the listing is complete.
    public static string? ContinuationToken(SformResult result)
    {
        ArgumentNullException.ThrowIfNull(result);
        var match = NextPagePattern().Match(result.PlainStdOut);
        return match.Success ? match.Groups["token"].Value.Trim() : null;
    }

    // The id of "Recipient '<id>' (<name>) added." (sform story kv recipients add).
    public static string RecipientIdOf(SformResult added, string name)
    {
        ArgumentNullException.ThrowIfNull(added);
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        var match = Regex.Match(added.PlainStdOut, $@"Recipient '(?<id>[^']+)' \({Regex.Escape(name)}\) added\.", RegexOptions.CultureInvariant);
        return match.Success ? match.Groups["id"].Value : throw added.Fail($"printed no \"Recipient '<id>' ({name}) added.\"");
    }

    // A new RSA-3072 key pair as PEM: the public key (SubjectPublicKeyInfo, what kv recipients add takes) and the private
    // key (PKCS#8, what --private-key takes).
    public static (string PublicPem, string PrivatePem) NewRsaKeyPair()
    {
        using var rsa = RSA.Create(3072);
        return (rsa.ExportSubjectPublicKeyInfoPem(), rsa.ExportPkcs8PrivateKeyPem());
    }

    // Runs the command until done says so, at most for the timeout (PolicyCacheWait by default), and returns the last
    // result: a call that depends on a policy change the instance answering it may not know yet. Each run is attached.
    public static async Task<SformResult> EventuallyAsync(Func<Task<SformResult>> run, Func<SformResult, bool> done, TimeSpan? timeout = null)
    {
        ArgumentNullException.ThrowIfNull(run);
        ArgumentNullException.ThrowIfNull(done);

        var cancellation = TestContext.Current?.Execution.CancellationToken ?? CancellationToken.None;
        var deadline = DateTimeOffset.UtcNow + (timeout ?? PolicyCacheWait);
        while (true)
        {
            var result = await run();
            if (done(result) || DateTimeOffset.UtcNow >= deadline)
            {
                return result;
            }

            await Task.Delay(PollInterval, cancellation);
        }
    }

    private static string? FirstCell(string line)
    {
        var cells = line.Split((char[]?)null, 2, StringSplitOptions.RemoveEmptyEntries);
        return cells.Length == 0 ? null : cells[0];
    }

    [GeneratedRegex(@"next page with -k (?<token>.+?)\s*$", RegexOptions.Multiline | RegexOptions.CultureInvariant)]
    private static partial Regex NextPagePattern();
}
