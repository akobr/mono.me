using Newtonsoft.Json;
using Newtonsoft.Json.Converters;

namespace _42.Platform.Storyteller;

// Bulk and structural operations a machine gets only when they are granted explicitly; never part of a
// MachineAccessScope preset. Each adds to the runtime scopes the operation already needs.
[JsonConverter(typeof(StringEnumConverter))]
public enum MachineManagementScope
{
    // Configuration.Transfer: the export and import of a view and of key/values.
    ConfigurationTransfer = 0,

    // Schema.Write: the writes and deletes of type, annotation and descendant-type schemas.
    SchemaWrite = 1,

    // Template.Write: the writes, patches and deletes of templates.
    TemplateWrite = 2,

    // Annotation.Manage: bulk create, soft delete and restore of annotations.
    AnnotationManage = 3,

    // KeyValue.Manage: key/value protect and bulk restore.
    KeyValueManage = 4,
}
