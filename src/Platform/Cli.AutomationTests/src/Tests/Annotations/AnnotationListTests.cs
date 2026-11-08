using _42.Platform.Cli.AutomationTests.Access;
using _42.Platform.Cli.AutomationTests.Data;
using _42.Platform.Cli.AutomationTests.Running;
using _42.Testing.UI.Automation;
using _42.Testing.UI.Automation.Runtime.TUnit;

namespace _42.Platform.Cli.AutomationTests.Tests.Annotations;

// List and Query: sform storyteller (story, annotations) lists, queries and pages the annotations of a view.
// The Reader cases read what an Editor created for them in a view of their own in Target:Project (a view exists by its
// data): the default view holds every annotation the runs ever created, more than a page, so a list there would show
// an arbitrary page of it.
[Area(Area.Annotations)]
public sealed class AnnotationListTests : CliTest
{
    // More than two pages of 1000 (the page of the API's lists), so the second page ends with a token too.
    private const int BulkCount = 2001;

    // The import of the bulk annotations: one job of about BulkCount writes.
    private static readonly TimeSpan BulkImportTimeout = TimeSpan.FromMinutes(25);

    // ANN-LST-01: storyteller (and story, annotations) lists the annotations of the view.
    // An Editor creates <rst> and its unit in a view of their own; as a Reader, sform storyteller, sform story and sform
    // annotations each list exactly those two keys under "# Annotations".
    [Test]
    [TestCase(235)]
    [UserRole(Role.Reader)]
    [Covers("storyteller")]
    [Covers("story")]
    [Covers("annotations")]
    public async Task Story_List_ShowsAnnotations()
    {
        var editor = await ArrangeInOwnViewAsync();
        var key = CliData.ResponsibilityKey();
        var unit = CliData.UnitKeyOf(key);
        await editor.Sform(InProject("story", "set", key)).ShouldSucceed();
        await editor.Sform(InProject("story", "set", unit)).ShouldSucceed();

        foreach (var command in new[] { "storyteller", "story", "annotations" })
        {
            var list = (await Sform(InProject(command))).ShouldSucceed().ShouldHaveLine("# Annotations");
            await Assert.That(AnnotationOutput.Keys(list)).IsEquivalentTo(new[] { key, unit });
        }
    }

    // ANN-LST-02: -r and -s query responsibilities and subjects by name.
    // An Editor creates rst.<name> and sbt.<name> (the common name part) and a responsibility and a subject of another
    // name; as a Reader, story -r <name> and --responsibilities <name> list only rst.<name> under "# Responsibilities",
    // story -s <name> and --subjects <name> only sbt.<name> under "# Subjects".
    [Test]
    [TestCase(251)]
    [UserRole(Role.Reader)]
    [Covers("story", "-r", "--responsibilities", "-s", "--subjects")]
    public async Task Story_QueryByName_Filters()
    {
        var editor = await ArrangeInOwnViewAsync();
        var name = CliData.AnnotationPart();
        var other = CliData.AnnotationPart();
        var responsibility = CliData.AnnotationKey(CliData.Responsibility, name);
        var subject = CliData.AnnotationKey(CliData.Subject, name);
        foreach (var key in new[] { responsibility, subject, CliData.AnnotationKey(CliData.Responsibility, other), CliData.AnnotationKey(CliData.Subject, other) })
        {
            await editor.Sform(InProject("story", "set", key)).ShouldSucceed();
        }

        foreach (var option in new[] { "-r", "--responsibilities" })
        {
            var query = (await Sform(InProject("story", option, name))).ShouldSucceed();
            await Assert.That(AnnotationOutput.Section(query, "Responsibilities")).IsEquivalentTo(new[] { responsibility });
            await Assert.That(AnnotationOutput.Keys(query)).IsEquivalentTo(new[] { responsibility });
        }

        foreach (var option in new[] { "-s", "--subjects" })
        {
            var query = (await Sform(InProject("story", option, name))).ShouldSucceed();
            await Assert.That(AnnotationOutput.Section(query, "Subjects")).IsEquivalentTo(new[] { subject });
            await Assert.That(AnnotationOutput.Keys(query)).IsEquivalentTo(new[] { subject });
        }
    }

    // ANN-LST-03: -u adds the usages of the queried responsibilities or subjects.
    // An Editor creates the usage usg.<s>.<r> below sbt.<s> and rst.<r>, and another usage of other names; as a Reader,
    // story -s <s> -u lists sbt.<s> under "# Subjects" and only usg.<s>.<r> under "# Usages", and story -r <r> --usages
    // lists rst.<r> under "# Responsibilities" and the same usage under "# Usages".
    [Test]
    [TestCase(252)]
    [UserRole(Role.Reader)]
    [Covers("story", "-u", "--usages")]
    public async Task Story_Usages_Combined()
    {
        var editor = await ArrangeInOwnViewAsync();
        var subjectName = CliData.AnnotationPart();
        var responsibilityName = CliData.AnnotationPart();
        var otherSubjectName = CliData.AnnotationPart();
        var otherResponsibilityName = CliData.AnnotationPart();
        var subject = CliData.AnnotationKey(CliData.Subject, subjectName);
        var responsibility = CliData.AnnotationKey(CliData.Responsibility, responsibilityName);
        var usage = CliData.AnnotationKey(CliData.Usage, subjectName, responsibilityName);
        var keys = new[]
        {
            subject,
            responsibility,
            usage,
            CliData.AnnotationKey(CliData.Subject, otherSubjectName),
            CliData.AnnotationKey(CliData.Responsibility, otherResponsibilityName),
            CliData.AnnotationKey(CliData.Usage, otherSubjectName, otherResponsibilityName),
        };
        foreach (var key in keys)
        {
            await editor.Sform(InProject("story", "set", key)).ShouldSucceed();
        }

        var bySubject = (await Sform(InProject("story", "-s", subjectName, "-u"))).ShouldSucceed();
        await Assert.That(AnnotationOutput.Section(bySubject, "Subjects")).IsEquivalentTo(new[] { subject });
        await Assert.That(AnnotationOutput.Section(bySubject, "Usages")).IsEquivalentTo(new[] { usage });

        var byResponsibility = (await Sform(InProject("story", "-r", responsibilityName, "--usages"))).ShouldSucceed();
        await Assert.That(AnnotationOutput.Section(byResponsibility, "Responsibilities")).IsEquivalentTo(new[] { responsibility });
        await Assert.That(AnnotationOutput.Section(byResponsibility, "Usages")).IsEquivalentTo(new[] { usage });
    }

    // ANN-LST-04: -c queries contexts, and -e adds executions.
    // An Editor creates the contexts cnt.<s>.<c> and cnt.<s>.<c2> and their executions exe.<s>.<r>.<c> and
    // exe.<s>.<r>.<c2> (with the subject, the responsibility and the usage above them); as a Reader, story -c <c> lists
    // only cnt.<s>.<c>; story --contexts <c> -e lists it under "# Contexts" and then its execution only under
    // "# Executions"; story -r <r> --executions lists rst.<r> and both executions.
    [Test]
    [TestCase(253)]
    [UserRole(Role.Reader)]
    [Covers("story", "-c", "--contexts", "-e", "--executions")]
    public async Task Story_ContextsAndExecutions()
    {
        var editor = await ArrangeInOwnViewAsync();
        var tree = await ArrangeExecutionsAsync(editor);

        var contexts = (await Sform(InProject("story", "-c", tree.ContextName))).ShouldSucceed();
        await Assert.That(AnnotationOutput.Section(contexts, "Contexts")).IsEquivalentTo(new[] { tree.Context });
        await Assert.That(AnnotationOutput.Keys(contexts)).IsEquivalentTo(new[] { tree.Context });

        var withExecutions = (await Sform(InProject("story", "--contexts", tree.ContextName, "-e"))).ShouldSucceed();
        await Assert.That(AnnotationOutput.Section(withExecutions, "Contexts")).IsEquivalentTo(new[] { tree.Context });
        await Assert.That(AnnotationOutput.Section(withExecutions, "Executions")).IsEquivalentTo(new[] { tree.Execution });
        await Assert.That(AnnotationOutput.IndexOfLine(withExecutions.Lines, "# Contexts"))
            .IsLessThan(AnnotationOutput.IndexOfLine(withExecutions.Lines, "# Executions"));

        var byResponsibility = (await Sform(InProject("story", "-r", tree.ResponsibilityName, "--executions"))).ShouldSucceed();
        await Assert.That(AnnotationOutput.Section(byResponsibility, "Responsibilities")).IsEquivalentTo(new[] { tree.Responsibility });
        await Assert.That(AnnotationOutput.Section(byResponsibility, "Executions")).IsEquivalentTo(new[] { tree.Execution, tree.OtherExecution });
    }

    // ANN-LST-05: -d lists the descendants of one annotation.
    // An Editor creates <rst> with two units and another responsibility with a unit; as a Reader, story -d <rst> and
    // story --descendants <rst> list the two units of <rst> under "# All descendants", and nothing else.
    [Test]
    [TestCase(254)]
    [UserRole(Role.Reader)]
    [Covers("story", "-d", "--descendants")]
    public async Task Story_Descendants_Listed()
    {
        var editor = await ArrangeInOwnViewAsync();
        var key = CliData.ResponsibilityKey();
        var units = new[] { CliData.UnitKeyOf(key), CliData.UnitKeyOf(key) };
        var other = CliData.ResponsibilityKey();
        foreach (var annotation in new[] { key, units[0], units[1], other, CliData.UnitKeyOf(other) })
        {
            await editor.Sform(InProject("story", "set", annotation)).ShouldSucceed();
        }

        foreach (var option in new[] { "-d", "--descendants" })
        {
            var descendants = (await Sform(InProject("story", option, key))).ShouldSucceed();
            await Assert.That(AnnotationOutput.Section(descendants, "All descendants")).IsEquivalentTo(units);
            await Assert.That(AnnotationOutput.Keys(descendants)).IsEquivalentTo(units);
        }
    }

    // ANN-LST-06: -t renders a tree by subjects, and -b picks the tree's root type.
    // An Editor creates sbt.<s>, rst.<r>, their usage, the contexts <c1> and <c2> of <s> and the executions of <r> in
    // both. As a Reader, story -t and story --tree -b sbt print "Tree of subjects" with <s> [subject], below it the
    // contexts and below each context <r> [responsibility]; story -t --tree-root rst and story -t -b responsibility print
    // "Tree of responsibilities" with <r> [responsibility], below it <s> [subject] and below that both contexts (two, so
    // the tree keeps them: a subject with a single context is shown without it).
    [Test]
    [TestCase(255)]
    [UserRole(Role.Reader)]
    [Covers("story", "-t", "--tree", "-b", "--tree-root", "--tree-root=sbt", "--tree-root=rst", "--tree-root=responsibility")]
    public async Task Story_Tree_Rendered()
    {
        var editor = await ArrangeInOwnViewAsync();
        var tree = await ArrangeExecutionsAsync(editor);
        var subjectNode = $"{tree.SubjectName} [subject]";
        var responsibilityNode = $"{tree.ResponsibilityName} [responsibility]";
        var contextNode = $"{tree.ContextName} [context]";
        var otherContextNode = $"{tree.OtherContextName} [context]";

        foreach (var arguments in new[] { new[] { "story", "-t" }, new[] { "story", "--tree", "-b", "sbt" } })
        {
            var subjects = (await Sform(InProject(arguments))).ShouldSucceed().ShouldContain("Tree of subjects").ShouldNotContain("Tree of responsibilities");
            var lines = subjects.Lines;
            var subjectLine = AnnotationOutput.IndexOfLine(lines, subjectNode);
            var contextLine = AnnotationOutput.IndexOfLine(lines, contextNode);
            await Assert.That(subjectLine).IsGreaterThanOrEqualTo(0);
            await Assert.That(contextLine).IsGreaterThan(subjectLine);
            await Assert.That(AnnotationOutput.IndexOfLine(lines, responsibilityNode, contextLine + 1)).IsGreaterThan(contextLine);
            await Assert.That(AnnotationOutput.IndexOfLine(lines, otherContextNode)).IsGreaterThan(subjectLine);
        }

        foreach (var arguments in new[] { new[] { "story", "-t", "--tree-root", "rst" }, new[] { "story", "-t", "-b", "responsibility" } })
        {
            var responsibilities = (await Sform(InProject(arguments))).ShouldSucceed().ShouldContain("Tree of responsibilities").ShouldNotContain("Tree of subjects");
            var lines = responsibilities.Lines;
            var responsibilityLine = AnnotationOutput.IndexOfLine(lines, responsibilityNode);
            var subjectLine = AnnotationOutput.IndexOfLine(lines, subjectNode);
            await Assert.That(responsibilityLine).IsGreaterThanOrEqualTo(0);
            await Assert.That(subjectLine).IsGreaterThan(responsibilityLine);
            await Assert.That(AnnotationOutput.IndexOfLine(lines, contextNode)).IsGreaterThan(subjectLine);
            await Assert.That(AnnotationOutput.IndexOfLine(lines, otherContextNode)).IsGreaterThan(subjectLine);
        }
    }

    // ANN-LST-07: -k continues a long list with the printed continuation token.
    // The Owner imports 2001 responsibilities into a disposable project (story import of a folder of empty annotation
    // files, a page of the API is 1000). story prints the first page and a token; story -k <token> the next page and a
    // second token; story --continuation-token <token 2> the page after that. The pages repeat no key, and with the
    // pages that may follow (story -k until no token is printed) they hold every imported key.
    [Test]
    [TestCase(256)]
    [UserRole(Role.Owner)]
    [DisposableProject]
    [Covers("story", "-k", "--continuation-token")]
    public async Task Story_ContinuationToken_NextPage()
    {
        await CreateDisposableProjectAsync();
        var stem = CliData.AnnotationPart();
        var keys = Enumerable.Range(1, BulkCount)
            .Select(index => CliData.AnnotationKey(CliData.Responsibility, $"{stem}-{index:D4}"))
            .ToList();
        foreach (var key in keys)
        {
            await WriteFileAsync($"bulk/{key}.json", "{}");
        }

        (await Sform(
            new SformRunOptions { Timeout = BulkImportTimeout, Label = $"import {BulkCount} responsibilities" },
            InProject("story", "import", "bulk", "--timeout", "20")))
            .ShouldSucceed();

        var first = (await Sform(InProject("story"))).ShouldSucceed();
        var token = AnnotationOutput.ContinuationToken(first) ?? throw first.Fail($"printed no continuation token for a view of {BulkCount} annotations");
        var second = (await Sform(InProject("story", "-k", token))).ShouldSucceed();
        var secondToken = AnnotationOutput.ContinuationToken(second) ?? throw second.Fail($"printed no continuation token on the second page of {BulkCount} annotations");
        await Assert.That(secondToken).IsNotEqualTo(token);
        var third = (await Sform(InProject("story", "--continuation-token", secondToken))).ShouldSucceed();

        var pages = new List<IReadOnlyList<string>> { AnnotationOutput.Keys(first), AnnotationOutput.Keys(second), AnnotationOutput.Keys(third) };
        var next = AnnotationOutput.ContinuationToken(third);
        for (var page = 0; next is not null && page < 20; page++)
        {
            var more = (await Sform(InProject("story", "-k", next))).ShouldSucceed();
            pages.Add(AnnotationOutput.Keys(more));
            next = AnnotationOutput.ContinuationToken(more);
        }

        var listed = pages.SelectMany(keysOfPage => keysOfPage).ToList();
        await Assert.That(listed.Count).IsEqualTo(listed.Distinct(StringComparer.Ordinal).Count());
        await Assert.That(listed).IsEquivalentTo(keys);
    }

    // ANN-LST-08: -i, --deleted and -a include inactive, deleted, or all annotations.
    // An Editor creates an active, a disabled ("IsDisabled": true through story set -i), a scheduled ("ValidFrom" in
    // 2099) and a deleted responsibility. As a Reader, story lists only the active one; story -i and --inactive add the
    // disabled and the scheduled one, marked [disabled] and [scheduled]; story --deleted adds only the deleted one, marked
    // [deleted]; story -a and --all list all four.
    [Test]
    [TestCase(257)]
    [UserRole(Role.Reader)]
    [Covers("story", "-i", "--inactive", "--deleted", "-a", "--all")]
    public async Task Story_IncludeInactiveDeletedAll()
    {
        var editor = await ArrangeInOwnViewAsync();
        var active = CliData.ResponsibilityKey();
        var disabled = CliData.ResponsibilityKey();
        var scheduled = CliData.ResponsibilityKey();
        var deleted = CliData.ResponsibilityKey();
        await WriteFileAsync("disabled.json", """{ "IsDisabled": true }""");
        await WriteFileAsync("scheduled.json", """{ "ValidFrom": "2099-01-01T00:00:00+00:00" }""");
        await editor.Sform(InProject("story", "set", active)).ShouldSucceed();
        await editor.Sform(InProject("story", "set", disabled, "-i", "disabled.json")).ShouldSucceed();
        await editor.Sform(InProject("story", "set", scheduled, "-i", "scheduled.json")).ShouldSucceed();
        await editor.Sform(InProject("story", "set", deleted)).ShouldSucceed();
        await editor.Sform(InProject("story", "delete", deleted)).ShouldSucceed();

        var plain = (await Sform(InProject("story"))).ShouldSucceed();
        await Assert.That(AnnotationOutput.Keys(plain)).IsEquivalentTo(new[] { active });

        foreach (var option in new[] { "-i", "--inactive" })
        {
            var inactive = (await Sform(InProject("story", option)))
                .ShouldSucceed()
                .ShouldContain($"{disabled} [disabled]")
                .ShouldContain($"{scheduled} [scheduled]");
            await Assert.That(AnnotationOutput.Keys(inactive)).IsEquivalentTo(new[] { active, disabled, scheduled });
        }

        var withDeleted = (await Sform(InProject("story", "--deleted"))).ShouldSucceed().ShouldContain($"{deleted} [deleted]");
        await Assert.That(AnnotationOutput.Keys(withDeleted)).IsEquivalentTo(new[] { active, deleted });

        foreach (var option in new[] { "-a", "--all" })
        {
            var all = (await Sform(InProject("story", option)))
                .ShouldSucceed()
                .ShouldContain($"{disabled} [disabled]")
                .ShouldContain($"{scheduled} [scheduled]")
                .ShouldContain($"{deleted} [deleted]");
            await Assert.That(AnnotationOutput.Keys(all)).IsEquivalentTo(new[] { active, disabled, scheduled, deleted });
        }
    }

    // The Editor who creates the data of a Reader's case, and a view of their own in the acting project for it.
    private async Task<SformActor> ArrangeInOwnViewAsync()
    {
        var editor = await ArrangeAsEditorAsync();
        UseProject(ProjectKey, CliData.ViewName());
        return editor;
    }

    // sbt.<s>, rst.<r>, usg.<s>.<r>, the contexts cnt.<s>.<c> and cnt.<s>.<c2>, and the executions exe.<s>.<r>.<c> and
    // exe.<s>.<r>.<c2>, ancestors first.
    private async Task<ExecutionTree> ArrangeExecutionsAsync(SformActor editor)
    {
        var tree = new ExecutionTree(CliData.AnnotationPart(), CliData.AnnotationPart(), CliData.AnnotationPart(), CliData.AnnotationPart());
        foreach (var key in new[] { tree.Subject, tree.Responsibility, CliData.AnnotationKey(CliData.Usage, tree.SubjectName, tree.ResponsibilityName), tree.Context, tree.OtherContext, tree.Execution, tree.OtherExecution })
        {
            await editor.Sform(InProject("story", "set", key)).ShouldSucceed();
        }

        return tree;
    }

    private sealed record ExecutionTree(string SubjectName, string ResponsibilityName, string ContextName, string OtherContextName)
    {
        public string Subject => $"{CliData.Subject}.{SubjectName}";

        public string Responsibility => $"{CliData.Responsibility}.{ResponsibilityName}";

        public string Context => $"{CliData.Context}.{SubjectName}.{ContextName}";

        public string OtherContext => $"{CliData.Context}.{SubjectName}.{OtherContextName}";

        public string Execution => $"{CliData.Execution}.{SubjectName}.{ResponsibilityName}.{ContextName}";

        public string OtherExecution => $"{CliData.Execution}.{SubjectName}.{ResponsibilityName}.{OtherContextName}";
    }
}
