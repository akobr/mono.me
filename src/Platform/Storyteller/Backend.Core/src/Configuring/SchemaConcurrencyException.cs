namespace _42.Platform.Storyteller.Configuring;

public class SchemaConcurrencyException(string project, string view, string schemaId, int retries)
    : Exception($"Failed to update schema '{schemaId}' in project '{project}' and view '{view}' after multiple retries ({retries}) due to concurrent modifications.")
{
    public string Project { get; } = project;

    public string View { get; } = view;

    public string SchemaId { get; } = schemaId;
}
