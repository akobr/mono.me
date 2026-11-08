using System.Text.RegularExpressions;

namespace _42.Platform.Cli.AutomationTests.Coverage;

// The coverage of the command tree by the [Covers] of the tests (spec §4.2), with the catalogue's rules
// (docs/Platform/Cli.AutomationTests/test-cases.md, Covers):
//   - The path of a [Covers] is the command line as typed after sform, aliases allowed ("story kv set"; "sform" is the
//     root). It covers its command when it ends with the command's canonical name, every alias it types (anywhere in the
//     path), every group it names that only prints its help (the root, schema, schema type, …), and the context option -p
//     of its command: every case passes -p to a context command, so -p counts as covered by any case of the command, while
//     --projectKey, -v and --view need a case of their own.
//   - Its items belong to that command: an option form as written ("-r" and "--resolved" are two items), a value of an
//     option with a fixed set as "--option=value" (any form of the option, the value in any case, from
//     coverage-values.json), a positional argument as arg0, arg1, ….
//   - A [Covers] counts whether its test runs or skips; an item that only feature-gated tests ([RequiresFeature]) or
//     skipped ones ([Skip]) cover belongs in coverage-exceptions.json.
public sealed partial class CoverageModel
{
    public CoverageModel(CommandTree tree, CoverageValues values)
    {
        Tree = tree;
        Values = values;
        Items = [.. BuildItems()];
    }

    public CommandTree Tree { get; }

    public CoverageValues Values { get; }

    // Every item to cover, command by command in the tree's order.
    public IReadOnlyList<CoverageItem> Items { get; }

    public ResolvedCovers Resolve(CoversUsage usage)
    {
        ArgumentNullException.ThrowIfNull(usage);

        var problems = new List<string>();
        var items = new List<CoverageItem>();
        var commandTokens = new List<string>();
        var itemTokens = usage.Covers.Items.Select(item => item.Trim()).ToList();

        var path = usage.Covers.Command.Trim();
        var all = path.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (all.Length == 0)
        {
            return new ResolvedCovers(usage, null, [], [], itemTokens, ["the command path is empty (\"sform\" is the root)."]);
        }

        var node = Tree.Root;
        var words = CommandTree.Words(path);
        if (all[0] == CliCommand.RootName)
        {
            commandTokens.Add(CliCommand.RootName);
            items.Add(new CoverageItem(node, CoverageKind.Command, node.Name));
        }

        for (var index = 0; index < words.Count; index++)
        {
            var word = words[index];
            var child = node.Child(word);
            if (child is null)
            {
                var known = node.Children.Count == 0 ? "it has none" : $"it has {string.Join(", ", node.Children.Select(Describe))}";
                problems.Add($"'{word}' is no subcommand of '{node.Path}' ({known}).");
                return new ResolvedCovers(usage, null, items, commandTokens, itemTokens, problems);
            }

            var isAlias = child.Name != word;
            var isLeaf = index == words.Count - 1;
            if (isAlias)
            {
                commandTokens.Add(word);
                items.Add(new CoverageItem(child, CoverageKind.Alias, word));
            }
            else if (isLeaf || child.IsHelpOnly)
            {
                commandTokens.Add(word);
                items.Add(new CoverageItem(child, CoverageKind.Command, child.Name));
            }

            node = child;
        }

        if (node.ContextProjectOption is not null)
        {
            items.Add(new CoverageItem(node, CoverageKind.Option, CommandTree.ContextProjectForm, "implicit: every case of a context command passes -p"));
        }

        // The item tokens as the catalogue writes them: a value by the option's long form and the value of
        // coverage-values.json ("-r=reader" is "--role=Reader"), anything else as written.
        var catalogueTokens = new List<string>(itemTokens.Count);
        foreach (var token in itemTokens)
        {
            if (ResolveItem(node, token, out var problem) is { } item)
            {
                items.Add(item);
                catalogueTokens.Add(item.Kind == CoverageKind.Value ? item.Token : token);
            }
            else
            {
                problems.Add(problem!);
                catalogueTokens.Add(token);
            }
        }

        return new ResolvedCovers(usage, node, items, commandTokens, catalogueTokens, problems);
    }

    // An item of a command by its token: an option form, "--option=value", argN, an alias, or null for the command itself.
    public CoverageItem? ResolveItem(CliCommand command, string? token, out string? problem)
    {
        ArgumentNullException.ThrowIfNull(command);
        problem = null;

        if (string.IsNullOrWhiteSpace(token))
        {
            return new CoverageItem(command, CoverageKind.Command, command.Name);
        }

        token = token.Trim();
        if (ArgumentToken().Match(token) is { Success: true } argument)
        {
            var index = int.Parse(argument.Groups[1].Value, System.Globalization.CultureInfo.InvariantCulture);
            if (index < command.Arguments.Count)
            {
                return new CoverageItem(command, CoverageKind.Argument, token, command.Arguments[index].Name);
            }

            problem = command.Arguments.Count == 0
                ? $"'{command.Path}' takes no argument, so there is no {token}."
                : $"'{command.Path}' takes {command.Arguments.Count} argument(s): {string.Join(", ", command.Arguments.Select(item => $"{item.Token} ({item.Name})"))}; there is no {token}.";
            return null;
        }

        if (token.StartsWith('-'))
        {
            var separator = token.IndexOf('=', StringComparison.Ordinal);
            var form = separator < 0 ? token : token[..separator];
            var option = command.FindOption(form);
            if (option is null)
            {
                problem = $"'{command.Path}' has no option {form} (its options: {(command.Options.Count == 0 ? "none" : string.Join(", ", command.Options))}).";
                return null;
            }

            if (separator < 0)
            {
                return new CoverageItem(command, CoverageKind.Option, form);
            }

            var value = token[(separator + 1)..];
            var entry = Values.For(command, option);
            if (entry is null)
            {
                problem = $"{option.Name} of '{command.Path}' has no fixed set of values in {CoverageValues.FileName}, so {token} is no item; cover the option as {form}.";
                return null;
            }

            var canonical = entry.Values.FirstOrDefault(known => string.Equals(known, value, StringComparison.OrdinalIgnoreCase));
            if (canonical is null)
            {
                problem = $"'{value}' is not a value of {option.Name} of '{command.Path}' ({string.Join(", ", entry.Values)}).";
                return null;
            }

            return new CoverageItem(command, CoverageKind.Value, $"{option.Name}={canonical}");
        }

        if (command.Aliases.Contains(token, StringComparer.Ordinal))
        {
            return new CoverageItem(command, CoverageKind.Alias, token);
        }

        if (token == command.Name)
        {
            return new CoverageItem(command, CoverageKind.Command, command.Name);
        }

        problem = $"'{token}' is neither an option (-x, --name, --name=value) nor an argument (arg0, arg1, …) of '{command.Path}'; a command or an alias belongs in the path.";
        return null;
    }

    public CoverageReport Evaluate(IReadOnlyList<CoversUsage> usages, CoverageExceptions exceptions)
    {
        ArgumentNullException.ThrowIfNull(usages);
        ArgumentNullException.ThrowIfNull(exceptions);

        var coveredBy = new Dictionary<string, List<CoversUsage>>(StringComparer.Ordinal);
        var invalid = new List<string>();
        foreach (var usage in usages)
        {
            if (!usage.IsTest)
            {
                invalid.Add($"{usage.TestName}: {usage.Written} is on a method that is not a [Test].");
            }

            var resolved = Resolve(usage);
            invalid.AddRange(resolved.Problems.Select(problem => $"{usage.TestName}: {usage.Written}: {problem}"));
            foreach (var item in resolved.Items)
            {
                if (!coveredBy.TryGetValue(item.Key, out var list))
                {
                    coveredBy[item.Key] = list = [];
                }

                if (!list.Contains(usage))
                {
                    list.Add(usage);
                }
            }
        }

        var uncovered = Items.Where(item => !coveredBy.ContainsKey(item.Key)).ToList();
        var gatedOnly = Items.Where(item => coveredBy.TryGetValue(item.Key, out var users) && users.All(user => user.IsGated)).ToList();

        var listed = new Dictionary<string, CoverageExceptions.Entry>(StringComparer.Ordinal);
        var stale = new List<string>();
        foreach (var entry in exceptions.Exceptions)
        {
            var command = entry.Command is null ? null : Tree.Find(entry.Command);
            if (command is null)
            {
                stale.Add($"{entry}: sform has no command '{entry.Command}'.");
                continue;
            }

            if (ResolveItem(command, entry.Item, out var problem) is not { } item)
            {
                stale.Add($"{entry}: {problem}");
                continue;
            }

            if (string.IsNullOrWhiteSpace(entry.Reason))
            {
                stale.Add($"{entry}: no reason.");
            }

            if (entry.Feature is { Length: > 0 } named && !CliFeatures.All.Contains(named, StringComparer.Ordinal))
            {
                stale.Add($"{entry}: the feature '{named}' is none of CliFeatures ({string.Join(", ", CliFeatures.All)}).");
            }

            if (!listed.TryAdd(item.Key, entry))
            {
                stale.Add($"{entry}: listed twice.");
                continue;
            }

            if (!coveredBy.TryGetValue(item.Key, out var users))
            {
                // Not covered at all yet: a gap of the coverage, not a stale entry (its feature-gated test is still to come).
                continue;
            }

            var running = users.Where(user => !user.IsGated).ToList();
            if (running.Count > 0)
            {
                stale.Add($"{entry}: covered by {string.Join(", ", running.Select(user => user.TestName))}, which run(s) against every target; remove the entry.");
            }
            else if (entry.Feature is { Length: > 0 } feature && users.Any(user => !user.IsSkipped && !user.Features.Contains(feature, StringComparer.Ordinal)))
            {
                stale.Add($"{entry}: listed for the feature {feature}, but covered by {string.Join(", ", users.Where(user => !user.Features.Contains(feature, StringComparer.Ordinal)).Select(user => $"{user.TestName} ([RequiresFeature({string.Join(", ", user.Features)})])"))}.");
            }
        }

        var unlisted = gatedOnly.Where(item => !listed.ContainsKey(item.Key)).ToList();
        return new CoverageReport(this, coveredBy.ToDictionary(pair => pair.Key, pair => (IReadOnlyList<CoversUsage>)pair.Value, StringComparer.Ordinal), uncovered, gatedOnly, unlisted, stale, invalid, usages.Count);
    }

    // What is wrong with coverage-values.json against the CLI: an entry that names no command or option, writes its command
    // with an alias (For would never find it), has no values or source, lists a value twice, or differs from its enum
    // ("enum", or the option's own enum type, but "except" and "refused", plus "extra"); and an option whose type is an
    // enum but has no entry.
    public IReadOnlyList<string> CheckValues()
    {
        var problems = new List<string>();
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var entry in Values.Options)
        {
            var command = entry.Command is null ? null : Tree.Find(entry.Command);
            if (command is null)
            {
                problems.Add($"{entry}: sform has no command '{entry.Command}'.");
                continue;
            }

            if (CoverageValues.Canonical(entry.Command!) != (command.IsRoot ? string.Empty : command.Path))
            {
                problems.Add($"{entry}: write the command's canonical path, \"{(command.IsRoot ? CliCommand.RootName : command.Path)}\"; with an alias in it the entry adds no value to cover.");
                continue;
            }

            var option = entry.Option is null ? null : command.FindOption(entry.Option);
            if (option is null)
            {
                problems.Add($"{entry}: '{command.Path}' has no option {entry.Option} (its options: {string.Join(", ", command.Options)}).");
                continue;
            }

            if (!seen.Add($"{command.Path}|{option.Name}"))
            {
                problems.Add($"{entry}: the option is listed twice.");
            }

            if (entry.Values.Count == 0)
            {
                problems.Add($"{entry}: no values.");
            }

            if (string.IsNullOrWhiteSpace(entry.Source))
            {
                problems.Add($"{entry}: no source (where the CLI's code keeps the set).");
            }

            var duplicates = entry.Values.GroupBy(value => value, StringComparer.OrdinalIgnoreCase).Where(group => group.Count() > 1).Select(group => group.Key).ToList();
            if (duplicates.Count > 0)
            {
                problems.Add($"{entry}: {string.Join(", ", duplicates)} listed twice.");
            }

            if (option.EnumType is not null && entry.Enum is not null && entry.Enum != option.EnumType)
            {
                problems.Add($"{entry}: \"enum\" is {entry.Enum}, but the option's type is the enum {option.EnumType}.");
            }

            var enumType = entry.Enum ?? option.EnumType;
            if (enumType is null)
            {
                if (entry.Except.Count > 0 || entry.Refused.Count > 0 || entry.Extra.Count > 0)
                {
                    problems.Add($"{entry}: \"except\", \"refused\" and \"extra\" are members of an enum, and the entry names none; list only the values it takes, and say in \"source\" what it refuses.");
                }

                continue;
            }

            var names = entry.Enum is null ? option.EnumNames : Tree.Enums.GetValueOrDefault(entry.Enum);
            if (names is null)
            {
                problems.Add($"{entry}: the enum {enumType} is not in 42.Platform.Cli.dll or the 42.*.dll next to it.");
                continue;
            }

            foreach (var (field, listed) in new[] { ("except", entry.Except), ("refused", entry.Refused) })
            {
                var unknown = listed.Where(name => !names.Contains(name, StringComparer.OrdinalIgnoreCase)).ToList();
                if (unknown.Count > 0)
                {
                    problems.Add($"{entry}: \"{field}\" names {string.Join(", ", unknown)}, which {enumType} does not have.");
                }
            }

            var both = entry.Except.Intersect(entry.Refused, StringComparer.OrdinalIgnoreCase).ToList();
            if (both.Count > 0)
            {
                problems.Add($"{entry}: {string.Join(", ", both)} in both \"except\" (the CLI refuses it) and \"refused\" (the platform refuses it); keep the one that is true.");
            }

            var left = entry.Except.Concat(entry.Refused).ToList();
            var expected = names.Where(name => !left.Contains(name, StringComparer.OrdinalIgnoreCase)).Concat(entry.Extra).ToList();
            var missing = expected.Where(name => !entry.Values.Contains(name, StringComparer.OrdinalIgnoreCase)).ToList();
            var extra = entry.Values.Where(value => !expected.Contains(value, StringComparer.OrdinalIgnoreCase)).ToList();
            if (missing.Count > 0 || extra.Count > 0)
            {
                problems.Add($"{entry}: the values differ from the members of {enumType}{(entry.Except.Count > 0 ? $" except {string.Join(", ", entry.Except)}" : string.Empty)}{(entry.Refused.Count > 0 ? $" and the refused {string.Join(", ", entry.Refused)}" : string.Empty)}{(entry.Extra.Count > 0 ? $" plus {string.Join(", ", entry.Extra)}" : string.Empty)}: {(missing.Count > 0 ? $"missing {string.Join(", ", missing)}" : string.Empty)}{(missing.Count > 0 && extra.Count > 0 ? "; " : string.Empty)}{(extra.Count > 0 ? $"not a member {string.Join(", ", extra)}" : string.Empty)}.");
            }
        }

        foreach (var command in Tree.Commands)
        {
            foreach (var option in command.Options.Where(option => option.EnumType is not null))
            {
                if (Values.For(command, option) is null)
                {
                    problems.Add($"{command.Path} {option.Name}: its type is the enum {option.EnumType} ({string.Join(", ", option.EnumNames ?? [])}); list it in {CoverageValues.FileName} so each value is covered.");
                }
            }
        }

        return problems;
    }

    private static string Describe(CliCommand command)
    {
        return command.Aliases.Count == 0 ? command.Name : $"{command.Name} ({string.Join(", ", command.Aliases)})";
    }

    [GeneratedRegex(@"^arg(\d+)$")]
    private static partial Regex ArgumentToken();

    private IEnumerable<CoverageItem> BuildItems()
    {
        foreach (var command in Tree.Commands)
        {
            yield return new CoverageItem(command, CoverageKind.Command, command.Name);
            foreach (var alias in command.Aliases)
            {
                yield return new CoverageItem(command, CoverageKind.Alias, alias);
            }

            foreach (var argument in command.Arguments)
            {
                yield return new CoverageItem(command, CoverageKind.Argument, argument.Token, argument.Name);
            }

            foreach (var option in command.Options)
            {
                foreach (var form in option.Forms)
                {
                    yield return new CoverageItem(command, CoverageKind.Option, form);
                }
            }

            foreach (var option in command.Options)
            {
                if (Values.For(command, option) is { } entry)
                {
                    foreach (var value in entry.Values)
                    {
                        yield return new CoverageItem(command, CoverageKind.Value, $"{option.Name}={value}");
                    }
                }
            }
        }
    }
}
