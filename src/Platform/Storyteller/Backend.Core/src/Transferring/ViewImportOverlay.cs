using Newtonsoft.Json.Linq;

namespace _42.Platform.Storyteller.Transferring;

// The view as the import will leave it, for the checks that look beyond one document. One import fills it in pass
// order, so it is not thread-safe:
// - the annotation imports record the keys they make active, also in a dry run, which writes nothing, so the later
//   passes ask here first;
// - the transfer service records the failed annotations, and after the annotation pass the schemas and
//   configurations the archive will write; a document that then fails or is skipped is removed again.
// The annotation keys are canonical (AnnotationKey.ToString()).
public sealed class ViewImportOverlay
{
    private readonly HashSet<string> _activeAnnotations = new(StringComparer.Ordinal);
    private readonly HashSet<string> _recreatedAnnotations = new(StringComparer.Ordinal);
    private readonly HashSet<string> _failedAnnotations = new(StringComparer.Ordinal);
    private readonly Dictionary<SchemaKey, JObject> _schemas = new();
    private readonly Dictionary<string, JObject> _configurations = new(StringComparer.Ordinal);

    // Keys the import makes active: created, or re-created.
    public IReadOnlySet<string> ActiveAnnotations => _activeAnnotations;

    // The deleted keys among them: their configurations are emptied by the re-create, so a configuration file is
    // Created.
    public IReadOnlySet<string> RecreatedAnnotations => _recreatedAnnotations;

    // Keys whose annotation failed in this import.
    public IReadOnlySet<string> FailedAnnotations => _failedAnnotations;

    // Schemas of the archive the import will write: valid, included, and allowed by Existing.
    public IReadOnlyDictionary<SchemaKey, JObject> Schemas => _schemas;

    // Configurations of the archive the import will write, by annotation key (their own documents): included, not
    // empty, allowed by Existing, and with an annotation.
    public IReadOnlyDictionary<string, JObject> Configurations => _configurations;

    // A key the import creates (or, when it was deleted, re-creates), the imported annotation or one of its ancestors.
    public void AddActiveAnnotation(AnnotationKey key, bool isRecreated = false)
    {
        var text = key.ToString();
        _activeAnnotations.Add(text);

        if (isRecreated)
        {
            _recreatedAnnotations.Add(text);
        }
    }

    public void AddFailedAnnotation(AnnotationKey key)
    {
        _failedAnnotations.Add(key.ToString());
    }

    public void SetSchema(SchemaKey key, JObject schema)
    {
        _schemas[key] = schema;
    }

    public bool RemoveSchema(SchemaKey key)
    {
        return _schemas.Remove(key);
    }

    public void SetConfiguration(AnnotationKey key, JObject document)
    {
        _configurations[key.ToString()] = document;
    }

    public bool RemoveConfiguration(AnnotationKey key)
    {
        return _configurations.Remove(key.ToString());
    }
}
