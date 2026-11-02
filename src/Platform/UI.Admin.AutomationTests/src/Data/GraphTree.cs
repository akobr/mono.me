namespace _42.Platform.UI.Admin.AutomationTests.Data;

// The tree of one execution exe.{s}.{r}.{c}: Bulk create of the execution alone also creates its missing ancestors
// rst.{r}, sbt.{s}, usg.{s}.{r} and cnt.{s}.{c} ("Created 5 annotations: 1 from the list and 4 missing ancestors.").
// Its links: rst.{r} and sbt.{s} to usg.{s}.{r}, sbt.{s} to cnt.{s}.{c}, and usg.{s}.{r} and cnt.{s}.{c} to the
// execution. In focus mode around the subject the graph draws all five at depth 2; around the context it draws three
// at depth 1 (cnt, sbt, exe), four at depth 2 (+ usg) and five at depth 3 (+ rst). Names are the last key parts: the
// subject's {s}, the usage's {r}, and the context's and the execution's {c}.
public sealed record GraphTree(string Execution)
{
    public string Responsibility => PlatformData.AncestorKey(Execution, PlatformData.Responsibility);

    public string Subject => PlatformData.AncestorKey(Execution, PlatformData.Subject);

    public string Usage => PlatformData.AncestorKey(Execution, PlatformData.Usage);

    public string Context => PlatformData.AncestorKey(Execution, PlatformData.Context);

    // {s}, the name of the subject.
    public string SubjectName => Execution.Split('.')[1];

    // {r}, the name of the responsibility and of the usage.
    public string ResponsibilityName => Execution.Split('.')[2];

    // {c}, the name of the context and of the execution.
    public string ContextName => Execution.Split('.')[3];

    // The execution's four ancestors, roots first: its lineage, and what the graph draws of the tree with Executions
    // hidden.
    public IReadOnlyList<string> Ancestors => [Responsibility, Subject, Usage, Context];
}
