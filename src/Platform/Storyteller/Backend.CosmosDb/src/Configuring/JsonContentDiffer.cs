using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using _42.Platform.Storyteller.Json;
using DiffPlex;
using DiffPlex.DiffBuilder;
using DiffPlex.DiffBuilder.Model;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace _42.Platform.Storyteller.Configuring;

internal sealed class JsonContentDiffer
{
    private readonly IJsonSerializationSettingsProvider _jsonSettingsProvider;

    public JsonContentDiffer(IJsonSerializationSettingsProvider jsonSettingsProvider)
    {
        _jsonSettingsProvider = jsonSettingsProvider;
    }

    public async Task<DiffResult> GetChangesAsync(
        Func<Task<JObject?>> fromProvider,
        Func<Task<JObject?>> toProvider,
        string fromErrorMessage,
        string toErrorMessage)
    {
        var fromJson = await fromProvider();

        if (fromJson is null)
        {
            throw new InvalidOperationException(fromErrorMessage);
        }

        var toJson = await toProvider();

        if (toJson is null)
        {
            throw new InvalidOperationException(toErrorMessage);
        }

        var serializerSettings = _jsonSettingsProvider.GetSettings(JsonSettingNames.Unique);
        var fromText = !fromJson.HasValues ? string.Empty : JsonConvert.SerializeObject(fromJson, Formatting.Indented, serializerSettings);
        var toText = !toJson.HasValues ? string.Empty : JsonConvert.SerializeObject(toJson, Formatting.Indented, serializerSettings);

        var diff = InlineDiffBuilder.Diff(fromText, toText);

        // Build annotated lines with line numbers
        var allLines = new List<DiffLine>();
        int oldLine = 0, newLine = 0;

        foreach (var line in diff.Lines)
        {
            var type = line.Type switch
            {
                ChangeType.Inserted => DiffChangeType.Addition,
                ChangeType.Deleted => DiffChangeType.Deletion,
                _ => DiffChangeType.Unchanged,
            };

            int? oldNum = type != DiffChangeType.Addition ? ++oldLine : null;
            int? newNum = type != DiffChangeType.Deletion ? ++newLine : null;

            allLines.Add(new DiffLine
            {
                Type = type,
                Content = line.Text,
                OldLineNumber = oldNum,
                NewLineNumber = newNum,
            });
        }

        // Compute word-level segments for modified pairs (deletion followed by insertion)
        ComputeWordSegments(allLines);

        // Group into hunks with 3-line context
        var hunks = BuildHunks(allLines, contextLines: 3);

        var stats = new DiffStats
        {
            Additions = allLines.Count(l => l.Type == DiffChangeType.Addition),
            Deletions = allLines.Count(l => l.Type == DiffChangeType.Deletion),
            Unchanged = allLines.Count(l => l.Type == DiffChangeType.Unchanged),
        };

        return new DiffResult { Stats = stats, Hunks = hunks };
    }

    private static void ComputeWordSegments(List<DiffLine> lines)
    {
        var differ = new Differ();

        for (int i = 0; i < lines.Count - 1; i++)
        {
            if (lines[i].Type != DiffChangeType.Deletion
                || lines[i + 1].Type != DiffChangeType.Addition)
            {
                continue;
            }

            var oldText = lines[i].Content;
            var newText = lines[i + 1].Content;
            var charDiff = differ.CreateCharacterDiffs(oldText, newText, false);

            lines[i] = lines[i] with
            {
                Segments = BuildSegments(oldText, charDiff.DiffBlocks, isOldSide: true),
            };

            lines[i + 1] = lines[i + 1] with
            {
                Segments = BuildSegments(newText, charDiff.DiffBlocks, isOldSide: false),
            };

            i++; // skip the addition line we just processed
        }
    }

    private static IReadOnlyList<DiffSegment> BuildSegments(
        string text,
        IList<DiffPlex.Model.DiffBlock> blocks,
        bool isOldSide)
    {
        var segments = new List<DiffSegment>();
        int pos = 0;

        foreach (var block in blocks)
        {
            int start = isOldSide ? block.DeleteStartA : block.InsertStartB;
            int count = isOldSide ? block.DeleteCountA : block.InsertCountB;

            // Add unchanged segment before this block
            if (start > pos)
            {
                segments.Add(new DiffSegment
                {
                    Text = text[pos..start],
                    IsChange = false,
                });
            }

            // Add changed segment
            if (count > 0)
            {
                var end = Math.Min(start + count, text.Length);
                segments.Add(new DiffSegment
                {
                    Text = text[start..end],
                    IsChange = true,
                });

                pos = end;
            }
            else
            {
                pos = start;
            }
        }

        // Add trailing unchanged segment
        if (pos < text.Length)
        {
            segments.Add(new DiffSegment
            {
                Text = text[pos..],
                IsChange = false,
            });
        }

        return segments;
    }

    private static IReadOnlyList<DiffHunk> BuildHunks(List<DiffLine> allLines, int contextLines)
    {
        if (allLines.Count == 0)
        {
            return [];
        }

        // Find indices of changed lines
        var changedIndices = new List<int>();
        for (int i = 0; i < allLines.Count; i++)
        {
            if (allLines[i].Type != DiffChangeType.Unchanged)
            {
                changedIndices.Add(i);
            }
        }

        if (changedIndices.Count == 0)
        {
            return [];
        }

        // Build hunk ranges (start/end indices into allLines) with context
        var hunkRanges = new List<(int Start, int End)>();
        int rangeStart = Math.Max(0, changedIndices[0] - contextLines);
        int rangeEnd = Math.Min(allLines.Count - 1, changedIndices[0] + contextLines);

        for (int i = 1; i < changedIndices.Count; i++)
        {
            int nextStart = Math.Max(0, changedIndices[i] - contextLines);
            int nextEnd = Math.Min(allLines.Count - 1, changedIndices[i] + contextLines);

            if (nextStart <= rangeEnd + 1)
            {
                // Merge with current range
                rangeEnd = nextEnd;
            }
            else
            {
                hunkRanges.Add((rangeStart, rangeEnd));
                rangeStart = nextStart;
                rangeEnd = nextEnd;
            }
        }

        hunkRanges.Add((rangeStart, rangeEnd));

        // Convert ranges to DiffHunks
        var hunks = new List<DiffHunk>();

        foreach (var (start, end) in hunkRanges)
        {
            var hunkLines = allLines.GetRange(start, end - start + 1);

            int oldStart = hunkLines.FirstOrDefault(l => l.OldLineNumber.HasValue)?.OldLineNumber ?? 0;
            int newStart = hunkLines.FirstOrDefault(l => l.NewLineNumber.HasValue)?.NewLineNumber ?? 0;
            int oldCount = hunkLines.Count(l => l.Type != DiffChangeType.Addition);
            int newCount = hunkLines.Count(l => l.Type != DiffChangeType.Deletion);

            hunks.Add(new DiffHunk
            {
                OldStart = oldStart,
                OldCount = oldCount,
                NewStart = newStart,
                NewCount = newCount,
                Lines = hunkLines,
            });
        }

        return hunks;
    }
}
