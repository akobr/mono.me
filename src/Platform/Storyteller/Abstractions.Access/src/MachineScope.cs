using Newtonsoft.Json;
using Newtonsoft.Json.Converters;

namespace _42.Platform.Storyteller;

// What a machine may do: a machine is exactly its set of scopes. The names are the wire format of the API and of
// the stored machines; claims and tokens carry the scope strings (Annotation.Read …) of MachineScopes.
[JsonConverter(typeof(StringEnumConverter))]
public enum MachineScope
{
    // Runtime: per-document reads and writes.
    AnnotationRead = 0,
    AnnotationWrite = 1,
    ConfigurationRead = 2,
    ConfigurationWrite = 3,

    // Key/value secrets; delivered to machines under the project's mTLS rule. They imply no Configuration scope.
    SecretsRead = 4,
    SecretsWrite = 5,

    // Management: bulk and structural operations, on top of their runtime scope.
    // Configuration.Transfer: the export and import of a view and of key/values.
    ConfigurationTransfer = 6,

    // Schema.Write: the writes and deletes of type, annotation and descendant-type schemas.
    SchemaWrite = 7,

    // Template.Write: the writes, patches and deletes of templates.
    TemplateWrite = 8,

    // Annotation.Manage: bulk create, soft delete and restore of annotations.
    AnnotationManage = 9,

    // KeyValue.Manage: key/value protect and bulk restore.
    KeyValueManage = 10,
}
