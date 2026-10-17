namespace _42.Platform.Storyteller.Transferring;

// What an export wrote: the annotations, the schemas, templates and configurations with a current document, and
// every version of the last three, the current ones included.
public sealed record class ViewArchiveStatistics(int AnnotationCount, int SchemaCount, int TemplateCount, int ConfigurationCount, int VersionCount);
