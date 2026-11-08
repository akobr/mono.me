namespace _42.Platform.Cli.AutomationTests.Coverage;

// Spec §4.2: every command, alias, option form, argument and fixed value of sform is covered by a [Covers] of a test. The
// command tree comes from the build of src/Platform/Cli/src (42.Platform.Cli.dll read as metadata, CommandTreeReader), the
// fixed values from coverage-values.json, the items only feature-gated tests may cover from coverage-exceptions.json.
// Plain TUnit tests (not CliTest): no target, so they run in every run, also in dotnet test --solution 42.mono.slnx, and a
// new option of the CLI without a test fails the run. Not in the Elementary set.
[Category(CliCategories.Coverage)]
public sealed class CommandCoverageTests
{
    // The reader sees the tree as McMaster builds it: the root, aliases, inherited context options, help-only groups and
    // arguments in order (a check of the reader itself, on commands that are stable).
    [Test]
    public async Task CommandTree_IsReadFromTheCli()
    {
        var tree = Context().Tree;

        await Assert.That(tree.Root.Name).IsEqualTo("sform");
        await Assert.That(tree.Root.IsHelpOnly).IsTrue();
        await Assert.That(tree.Root.FindOption("--version")).IsNotNull();

        var story = tree.Find("story");
        await Assert.That(story).IsNotNull();
        await Assert.That(story!.Name).IsEqualTo("storyteller");
        await Assert.That(story.ContextProjectOption).IsNotNull();
        await Assert.That(story.FindOption("--projectKey")!.DeclaringType).IsEqualTo(CommandTree.ContextCommandType);
        await Assert.That(story.FindOption("--view")).IsNotNull();

        await Assert.That(tree.Find("account set")!.ContextProjectOption).IsNull();
        await Assert.That(tree.Find("account set")!.FindOption("-p")).IsNotNull();
        await Assert.That(tree.Find("storyteller schema")!.IsHelpOnly).IsTrue();
        await Assert.That(tree.Find("storyteller schema type")!.IsHelpOnly).IsTrue();
        await Assert.That(tree.Find("assist")!.IsHelpOnly).IsFalse();
        await Assert.That(tree.Find("story config diff")!.Arguments.Select(argument => argument.Token).ToArray()).IsEquivalentTo(new[] { "arg0", "arg1", "arg2" });
        await Assert.That(tree.Find("storyteller schema type set")!.ContextProjectOption).IsNotNull();

        Console.WriteLine($"sform: {tree.Commands.Count} commands, {tree.Commands.Sum(command => command.Aliases.Count)} aliases, {tree.Commands.Sum(command => command.Options.Count)} options ({tree.Commands.Sum(command => command.Options.Sum(option => option.Forms.Count))} forms), {tree.Commands.Sum(command => command.Arguments.Count)} arguments; {Context().Model.Items.Count} items to cover.");
    }

    // Every [Covers] names a command path, options, values and arguments sform has, and sits on a test.
    [Test]
    public async Task Covers_NameOnlyWhatSformHas()
    {
        var report = Report();

        if (report.InvalidCovers.Count > 0)
        {
            Assert.Fail($"{report.InvalidCovers.Count} [Covers] name something sform does not have:\n  {string.Join("\n  ", report.InvalidCovers)}");
        }

        await Assert.That(report.Usages).IsGreaterThan(0);
    }

    // coverage-values.json names existing options, gives each a source, and matches the enum the code parses into.
    [Test]
    public async Task CoverageValues_MatchTheCli()
    {
        var problems = Context().Model.CheckValues();

        if (problems.Count > 0)
        {
            Assert.Fail($"{CoverageValues.FileName} does not match the CLI ({problems.Count}):\n  {string.Join("\n  ", problems)}");
        }

        await Assert.That(Context().Model.Values.Options.Count).IsGreaterThan(0);
    }

    // coverage-exceptions.json lists exactly the items only feature-gated (or skipped) tests cover.
    [Test]
    public async Task CoverageExceptions_ListTheFeatureGatedItems()
    {
        var report = Report();

        if (report.UnlistedExceptions.Count > 0 || report.StaleExceptions.Count > 0)
        {
            Assert.Fail(report.DescribeExceptions());
        }

        await Assert.That(report.GatedOnly.Count).IsGreaterThanOrEqualTo(0);
    }

    // The gap: what no test covers yet, grouped by command.
    [Test]
    public async Task EveryCommandOptionArgumentAndValue_IsCovered()
    {
        var report = Report();

        if (report.Uncovered.Count > 0)
        {
            Assert.Fail(report.DescribeUncovered());
        }

        await Assert.That(report.Uncovered.Count).IsEqualTo(0);
    }

    private static CoverageContext Context()
    {
        if (CoverageContext.Unavailable() is { } reason)
        {
            Skip.Test(reason);
        }

        return CoverageContext.Current;
    }

    private static CoverageReport Report()
    {
        var context = Context();
        return context.Model.Evaluate(context.Usages, context.Exceptions);
    }
}
