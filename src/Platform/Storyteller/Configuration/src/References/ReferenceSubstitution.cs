using System.Collections.Concurrent;
using System.Diagnostics.CodeAnalysis;
using _42.Platform.Storyteller.Sdk.Configuring.Flattening;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json.Linq;

namespace _42.Platform.Storyteller.Sdk.Configuring.References;

// The substitution step of the apply pipeline: after flattening and validation, before the swap. In Resolved mode, with
// a resolver, it walks the string values of a document, never its property names, and replaces every value the resolver
// accepts with its secret. A failure fails the apply with its class; the text of an accepted reference is never applied.
// Resolved values are kept in memory per value text, with their resolution time, and reused until
// SecretRefreshInterval: in a cache shared by every document of the provider, or only in the results, so that the
// document service drops the secrets of a document with its entry. They are never logged, written to disk or put in an
// exception: messages name the path, the vault and the secret.
internal sealed class ReferenceSubstitution
{
    // More references than this are counted, not listed.
    private const int MaxListed = 20;

    private readonly ISecretReferenceResolver? _resolver;
    private readonly StorytellerReadMode _mode;
    private readonly TimeSpan _refreshInterval;
    private readonly TimeProvider _time;

    // Null when the values live in the results only.
    private readonly ConcurrentDictionary<string, ResolvedValue>? _shared;

    public ReferenceSubstitution(ISecretReferenceResolver? resolver, StorytellerReadMode mode, TimeSpan refreshInterval, TimeProvider time, bool sharesValues = true)
    {
        _resolver = resolver;
        _mode = mode;
        _refreshInterval = refreshInterval;
        _time = time;
        _shared = sharesValues ? new ConcurrentDictionary<string, ResolvedValue>(StringComparer.Ordinal) : null;
    }

    public ValueTask<SubstitutionResult> SubstituteAsync(JObject serverContent, CancellationToken cancellationToken)
    {
        return SubstituteAsync(serverContent, null, cancellationToken);
    }

    // The content to flatten and apply: serverContent itself when nothing was resolved, otherwise a copy. Never changes
    // serverContent: the cache keeps the server's document. reuse: an earlier result of the same document, whose fresh
    // values are reused. The token bounds resolver calls only: a startup that falls back to the cache after its read used
    // up ReadTimeout still applies an entry that needs no resolution. Throws SecretResolutionException, with its class,
    // for a failure; the token's cancellation comes out as it is.
    public async ValueTask<SubstitutionResult> SubstituteAsync(JObject serverContent, SubstitutionResult? reuse, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(serverContent);

        if (_mode != StorytellerReadMode.Resolved)
        {
            return new SubstitutionResult(serverContent, null);
        }

        var values = new List<(JValue Token, string Path, string Text)>();
        CollectStrings(serverContent, JsonPaths.Root, values);

        if (_resolver is not { } resolver)
        {
            // Left as written, with one warning per applied document.
            return new SubstitutionResult(serverContent, null)
            {
                Unresolved = [.. values.Where(value => SecretReference.TryParse(value.Text, out _)).Select(value => new UnresolvedReference(value.Path, null))],
            };
        }

        var now = _time.GetUtcNow();
        Prune(now);

        Dictionary<JValue, string>? replacements = null;
        Dictionary<string, ResolvedValue>? used = null;
        List<UnresolvedReference>? unresolved = null;
        DateTimeOffset? oldest = null;

        foreach (var (token, path, text) in values)
        {
            if (!Accepts(resolver, text, path))
            {
                if (SecretReference.TryParse(text, out var reference))
                {
                    // The shorthand is the platform's own and unambiguous: an unclaimed one is a missing resolver.
                    if (reference.Form == SecretReferenceForm.Shorthand)
                    {
                        throw new SecretResolutionException(
                            $"The value at {path} is a kv:// reference to the secret {reference.Name} of the vault {reference.VaultName}, which no resolver accepts. Set a ReferenceResolver that resolves it, or allow the vault in the one that is set.",
                            false,
                            reference.VaultName,
                            reference.Name);
                    }

                    (unresolved ??= []).Add(new UnresolvedReference(path, reference.VaultUri!.Host));
                }

                continue;
            }

            if (!TryReuse(text, used, reuse, now, out var resolved))
            {
                resolved = await ResolveAsync(resolver, text, path, cancellationToken).ConfigureAwait(false);
            }

            (replacements ??= new Dictionary<JValue, string>(ReferenceEqualityComparer.Instance))[token] = resolved.Value;
            (used ??= new Dictionary<string, ResolvedValue>(StringComparer.Ordinal))[text] = resolved;
            oldest = oldest is { } current && current <= resolved.ResolvedAt ? current : resolved.ResolvedAt;
        }

        var content = replacements is null ? serverContent : (JObject)Copy(serverContent, replacements);
        var result = new SubstitutionResult(content, oldest) { Unresolved = unresolved ?? [], HasResolver = true };
        return used is null ? result : result with { ResolvedValues = used };
    }

    // Step 2 of a run: whether a 304 must resolve the snapshot's references again. An infinite interval never expires.
    public bool IsRefreshDue(SubstitutionResult result)
    {
        ArgumentNullException.ThrowIfNull(result);

        return result.OldestResolution is { } resolvedAt
            && _resolver is not null
            && _mode == StorytellerReadMode.Resolved
            && _refreshInterval != Timeout.InfiniteTimeSpan
            && _time.GetUtcNow() - resolvedAt >= _refreshInterval;
    }

    // One warning per applied document: the values with the shape of a reference that stay as written, by path, and by
    // host when a resolver declined them. Never a value.
    public void WarnUnresolved(ILogger logger, string key, SubstitutionResult? result)
    {
        if (result is not { Unresolved.Count: > 0 } || !logger.IsEnabled(LogLevel.Warning))
        {
            return;
        }

        var listed = string.Join(
            ", ",
            result.Unresolved.Take(MaxListed).Select(reference => reference.Host is null ? reference.Path : $"{reference.Path} ({reference.Host})"));

        if (result.Unresolved.Count > MaxListed)
        {
            listed += ", …";
        }

        if (result.HasResolver)
        {
            logger.LogWarning(
                "The configuration {Key} has {Count} secret references that no resolver accepts, at {References}. They are applied as written.",
                key,
                result.Unresolved.Count,
                listed);
        }
        else
        {
            logger.LogWarning(
                "The configuration {Key} has {Count} values that look like secret references, at {Paths}, and no ReferenceResolver is set. They are applied as written.",
                key,
                result.Unresolved.Count,
                listed);
        }
    }

    private static void CollectStrings(JToken token, string path, List<(JValue Token, string Path, string Text)> values)
    {
        switch (token)
        {
            case JObject container:
                foreach (var property in container.Properties())
                {
                    CollectStrings(property.Value, JsonPaths.Property(path, property.Name), values);
                }

                break;

            case JArray array:
                for (var index = 0; index < array.Count; index++)
                {
                    CollectStrings(array[index], JsonPaths.Item(path, index), values);
                }

                break;

            // No reference is empty.
            case JValue { Type: JTokenType.String, Value: string text } value when !string.IsNullOrWhiteSpace(text):
                values.Add((value, path, text.Trim()));
                break;
        }
    }

    // Any exception of a resolver is its failure, and a definitive one unless it says otherwise.
    private static bool Accepts(ISecretReferenceResolver resolver, string text, string path)
    {
        try
        {
            return resolver.CanResolve(text);
        }
        catch (Exception exception)
        {
            throw new SecretResolutionException($"The resolver failed with {exception.GetType().Name} when it was asked about the value at {path}.", false, exception);
        }
    }

    private static JToken Copy(JToken token, Dictionary<JValue, string> replacements)
    {
        return token switch
        {
            JObject container => new JObject(container.Properties().Select(property => new JProperty(property.Name, Copy(property.Value, replacements)))),
            JArray array => new JArray(array.Select(item => Copy(item, replacements))),
            JValue value when replacements.TryGetValue(value, out var resolved) => new JValue(resolved),
            _ => token.DeepClone(),
        };
    }

    // A resolution that a resolver abandons on cancellation may fail later; nobody waits for it any more.
    private static void Observe(Task task)
    {
        _ = task.ContinueWith(
            static completed => _ = completed.Exception,
            CancellationToken.None,
            TaskContinuationOptions.OnlyOnFaulted | TaskContinuationOptions.ExecuteSynchronously,
            TaskScheduler.Default);
    }

    private async ValueTask<ResolvedValue> ResolveAsync(ISecretReferenceResolver resolver, string text, string path, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        Task<string>? resolving = null;
        string? value;

        try
        {
            resolving = resolver.ResolveAsync(text, cancellationToken).AsTask();

            // A resolver that ignores cancellation cannot hold the run past its ReadTimeout.
            value = await resolving.WaitAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            if (resolving is not null)
            {
                Observe(resolving);
            }

            throw;
        }
        catch (SecretResolutionException exception)
        {
            throw new SecretResolutionException(
                $"The secret reference at {path} could not be resolved: {exception.Message}",
                exception.IsTransient,
                exception.VaultName,
                exception.SecretName,
                exception);
        }
        catch (OperationCanceledException exception)
        {
            // A timeout of the resolver's own, which the caller did not request.
            throw new SecretResolutionException($"Resolving the secret reference at {path} timed out.", true, exception);
        }
        catch (Exception exception)
        {
            // Its message stays in the inner exception; it is not ours to vouch for.
            throw new SecretResolutionException($"Resolving the secret reference at {path} failed with {exception.GetType().Name}.", false, exception);
        }

        if (value is null)
        {
            throw new SecretResolutionException($"The resolver returned no value for the secret reference at {path}.", false);
        }

        var resolved = new ResolvedValue(value, _time.GetUtcNow());

        if (_shared is not null)
        {
            _shared[text] = resolved;
        }

        return resolved;
    }

    private bool IsFresh(ResolvedValue resolved, DateTimeOffset now)
    {
        return _refreshInterval == Timeout.InfiniteTimeSpan || now - resolved.ResolvedAt < _refreshInterval;
    }

    // A value this substitution resolved already, or a fresh one of the shared cache or of the earlier result of the same
    // document.
    private bool TryReuse(
        string text,
        Dictionary<string, ResolvedValue>? used,
        SubstitutionResult? reuse,
        DateTimeOffset now,
        [NotNullWhen(true)] out ResolvedValue? resolved)
    {
        if (used is not null && used.TryGetValue(text, out resolved))
        {
            return true;
        }

        if (_shared is not null && _shared.TryGetValue(text, out resolved) && IsFresh(resolved, now))
        {
            return true;
        }

        if (reuse is not null && reuse.ResolvedValues.TryGetValue(text, out resolved) && IsFresh(resolved, now))
        {
            return true;
        }

        resolved = null;
        return false;
    }

    // Expired values would be resolved again anyway; dropping them keeps secrets of documents that are gone out of memory.
    private void Prune(DateTimeOffset now)
    {
        if (_shared is null)
        {
            return;
        }

        foreach (var pair in _shared)
        {
            if (!IsFresh(pair.Value, now))
            {
                _shared.TryRemove(pair);
            }
        }
    }
}
