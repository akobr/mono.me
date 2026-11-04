using _42.Platform.Storyteller.Sdk.Hosting.Reading;
using Newtonsoft.Json.Linq;

namespace _42.Platform.Storyteller.Sdk.Hosting.UnitTests.Informer;

// The informer's reads without HTTP: list pages from a script by list and token, and point reads that find every key
// active. Records the tokens each list was asked for. Hooks run before an answer, to hold it or to fail it.
internal sealed class ScriptedPages : IAnnotationPages
{
    private readonly List<(AnnotationList List, string? Token)> _pages = [];
    private readonly List<string> _reads = [];
    private readonly Lock _lock = new();

    // The page of a list at a token (null: the first); throws as a failed page does.
    public Func<AnnotationList, string?, AnnotationPage> Pages { get; set; } = (_, _) => new AnnotationPage([], null);

    // Awaited before a page is answered, with the read's token; null: none.
    public Func<AnnotationList, string?, CancellationToken, Task>? BeforePage { get; set; }

    // Run before a point read is answered; throws as a failed read does. Null: none.
    public Action<FullKey>? BeforeRead { get; set; }

    public IReadOnlyList<(AnnotationList List, string? Token)> PageReads
    {
        get
        {
            lock (_lock)
            {
                return [.. _pages];
            }
        }
    }

    public IReadOnlyList<string> PointReads
    {
        get
        {
            lock (_lock)
            {
                return [.. _reads];
            }
        }
    }

    public static AnnotationPage Page(string? continuationToken, params Annotation[] items)
    {
        return new AnnotationPage(items, continuationToken);
    }

    // A list item; modifiedAt: its Modified stamp, none when null.
    public static Annotation Item(string annotationKey, string? modifiedAt = "2026-10-20T08:00:00Z")
    {
        var json = TestAnnotations.Json(annotationKey, json =>
        {
            if (modifiedAt is not null)
            {
                json["Modified"] = new JObject { ["At"] = modifiedAt, ["By"] = new JObject { ["Kind"] = "Account", ["Id"] = "user_1" } };
            }
        });

        return JObject.Parse(json).ToObject<Annotation>()!;
    }

    public async Task<AnnotationPage> ReadPageAsync(FullKey responsibility, AnnotationList list, string? continuationToken, CancellationToken cancellationToken)
    {
        lock (_lock)
        {
            _pages.Add((list, continuationToken));
        }

        if (BeforePage is { } before)
        {
            await before(list, continuationToken, cancellationToken);
        }

        return Pages(list, continuationToken);
    }

    public Task<StorytellerAnnotation> ReadAnnotationAsync(FullKey key, bool includeDeleted, CancellationToken cancellationToken)
    {
        lock (_lock)
        {
            _reads.Add(key.Annotation.ToString());
        }

        BeforeRead?.Invoke(key);
        var annotation = JObject.Parse(TestAnnotations.Json(key.Annotation.ToString())).ToObject<Annotation>()!;
        return Task.FromResult(AnnotationMapper.Map(key, annotation));
    }
}
