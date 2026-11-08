namespace _42.Platform.Storyteller.Sdk.Hosting;

// What IStorytellerCatalog lists and watches: the descendants of one type under one annotation, for example
// StorytellerTarget.UnitsOf("rst.invoicing") or StorytellerTarget.ExecutionsOf("sbt.northwind").WithContext("production").
// key: the ancestor's, a full key, or an annotation key that the catalog puts in the organization, project and view of
// the options' Key. Each factory throws ArgumentException for a key that does not parse, or one whose type the API does
// not list those descendants under: a responsibility has no contexts, a subject no units, a unit only units of
// execution, a usage and a context only executions and units of execution, an execution only units of execution, and a
// unit of execution nothing.
public static class StorytellerTarget
{
    // usg.{subject}.{responsibility}, under rst or sbt.
    public static StorytellerTarget<StorytellerUsage> UsagesOf(string key)
    {
        return new StorytellerTarget<StorytellerUsage>(key, AnnotationType.Usage);
    }

    // cnt.{subject}.{context}, under sbt.
    public static StorytellerTarget<StorytellerContext> ContextsOf(string key)
    {
        return new StorytellerTarget<StorytellerContext>(key, AnnotationType.Context);
    }

    // unt.{responsibility}.{unit}, under rst.
    public static StorytellerTarget<StorytellerUnit> UnitsOf(string key)
    {
        return new StorytellerTarget<StorytellerUnit>(key, AnnotationType.Unit);
    }

    // exe.{subject}.{responsibility}.{context}, under rst, sbt, usg or cnt.
    public static StorytellerTarget<StorytellerExecution> ExecutionsOf(string key)
    {
        return new StorytellerTarget<StorytellerExecution>(key, AnnotationType.Execution);
    }

    // uxe.{subject}.{responsibility}.{context}.{unit}, under any type but uxe.
    public static StorytellerTarget<StorytellerUnitOfExecution> UnitsOfExecutionOf(string key)
    {
        return new StorytellerTarget<StorytellerUnitOfExecution>(key, AnnotationType.UnitOfExecution);
    }
}
