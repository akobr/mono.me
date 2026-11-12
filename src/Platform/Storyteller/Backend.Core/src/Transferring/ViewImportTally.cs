namespace _42.Platform.Storyteller.Transferring;

// The counts of an import while it runs, in total and per included kind, and its items: every outcome except
// Unchanged, up to the limit. A result with warnings counts once in Warnings, unless it is Unchanged.
public sealed class ViewImportTally
{
    private readonly IReadOnlyList<ViewArchiveContent> _include;
    private readonly int _ignoredFiles;
    private readonly int _excludedFiles;
    private readonly int _maxItems;
    private readonly List<ViewImportItem> _items = [];
    private readonly Counts _total = new();
    private readonly Dictionary<ViewArchiveContent, Counts> _counts;

    // include: the kinds with a summary (null or empty means all four); total: the files to import; the ignored and
    // excluded files of the reader go to the total summary.
    public ViewImportTally(
        IEnumerable<ViewArchiveContent>? include,
        int total,
        int ignoredFiles = 0,
        int excludedFiles = 0,
        int maxItems = int.MaxValue)
    {
        _include = ViewArchiveInclude.Resolve(include);
        _ignoredFiles = ignoredFiles;
        _excludedFiles = excludedFiles;
        _maxItems = maxItems;
        _counts = _include.ToDictionary(content => content, _ => new Counts());
        Total = total;
    }

    public int Total { get; }

    public int Processed { get; private set; }

    public ViewImportSummary Summary => _total.ToSummary(null) with { ExcludedFiles = _excludedFiles, IgnoredFiles = _ignoredFiles };

    // One per included kind, in import order.
    public IReadOnlyList<ViewImportSummary> Summaries => _include.Select(content => _counts[content].ToSummary(content)).ToList();

    public IReadOnlyList<ViewImportItem> Items => _items.ToList();

    public bool AreItemsTruncated { get; private set; }

    public void Add(ViewArchiveFile file, ViewImportResult result)
    {
        Processed++;
        _total.Add(result);

        // a kind left out has no summary; the reader returns the included kinds only
        if (_counts.TryGetValue(ViewArchiveNames.GetContent(file.Kind), out var counts))
        {
            counts.Add(result);
        }

        if (result.Outcome == ViewImportOutcome.Unchanged)
        {
            return;
        }

        if (_items.Count >= _maxItems)
        {
            AreItemsTruncated = true;
            return;
        }

        _items.Add(new ViewImportItem
        {
            Path = file.Path,
            Kind = file.Kind,
            AnnotationType = file.AnnotationType,
            AnnotationKey = file.AnnotationKey?.ToString(),
            Outcome = result.Outcome,
            Version = result.Version,
            ErrorCode = result.ErrorCode,
            Message = result.Message,
            Warnings = result.Warnings,
        });
    }

    private sealed class Counts
    {
        private int _created;
        private int _updated;
        private int _unchanged;
        private int _skipped;
        private int _failed;
        private int _warnings;

        public void Add(ViewImportResult result)
        {
            switch (result.Outcome)
            {
                case ViewImportOutcome.Created:
                    _created++;
                    break;

                case ViewImportOutcome.Updated:
                    _updated++;
                    break;

                case ViewImportOutcome.Unchanged:
                    _unchanged++;
                    return;

                case ViewImportOutcome.Skipped:
                    _skipped++;
                    break;

                default:
                    _failed++;
                    break;
            }

            if (result.Warnings is { Count: > 0 })
            {
                _warnings++;
            }
        }

        public ViewImportSummary ToSummary(ViewArchiveContent? content)
        {
            return new ViewImportSummary
            {
                Content = content,
                Created = _created,
                Updated = _updated,
                Unchanged = _unchanged,
                Skipped = _skipped,
                Failed = _failed,
                Warnings = _warnings,
            };
        }
    }
}
