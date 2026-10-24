namespace _42.Platform.Storyteller.Mcp.Tools;

// Descriptions shared by the annotation, configuration, schema and template tools.
public static class ManagementText
{
    public const string Paged =
        "Returns {Items, ContinuationToken, Count}; pass ContinuationToken back for the next page. A page can be empty "
        + "and still carry a ContinuationToken, because the list is filtered page by page: follow it until it is absent. "
        + "Deleting, restoring or changing listed items between pages can skip items, so after acting on them start "
        + "again from the first page.";

    public const string CompactItems =
        "Items are compact: AnnotationKey, AnnotationType, Name, Title, Status (Active, Disabled, Scheduled or Expired), "
        + "Labels, Modified, and Deletion on deleted ones; get_annotation returns a whole annotation.";

    public const string IncludeInactive =
        "Also annotations that are not active now: disabled, scheduled (ValidFrom in the future) or expired (ExpiresAt "
        + "passed). Default false.";

    public const string IncludeDeleted =
        "Also soft-deleted annotations, each with its Deletion. Default false. A deleted annotation that is not active "
        + "also needs includeInactive.";

    public const string RecreateDeleted =
        "Re-create deleted keys and ancestors instead of refusing: they come back empty, their configurations go to the "
        + "versions, and their other deleted descendants stay deleted. Default false; prefer restore_annotations.";

    public const string Merge =
        "objects merge property by property, arrays are unioned (items already present are skipped), a null never "
        + "erases a value; a top-level \"$remove\" (an array of JSONPaths, for example [\"$.legacy\", \"$.hosts[0]\"]) "
        + "removes what they match and a top-level \"$patch\" (RFC 6902 operations) patches the merged document";

    public const string Content = "The JSON object to write.";

    public const string DesiredContent =
        "The JSON object the stored document must equal, as it should be stored; no \"$remove\" or \"$patch\" directives.";

    public const string Operations =
        "RFC 6902 operations as a JSON array, for example [{\"op\":\"test\",\"path\":\"/retries\",\"value\":3},"
        + "{\"op\":\"replace\",\"path\":\"/retries\",\"value\":5}]. Paths are JSON Pointers into the stored document. "
        + "Pointers with the escapes ~0 or ~1 are refused: the API applies them as literal characters.";

    public const string Force =
        "Store the result even when it violates the combined schema of the annotation (get_combined_schema). Default "
        + "false: a violation answers 409 with the messages and nothing is stored.";

    public const string OwnDocument =
        "Writes address the annotation's own stored document (get_configuration with form \"own\"), never the effective "
        + "one: writing back an edited effective document would copy every inherited value onto this annotation.";

    public const string TemplateType = "The annotation type code of the template: rst, unt, sbt, usg, cnt, exe or uxe.";

    public const string TemplatePosition =
        "A template is merged at every annotation of its type in the view, after the annotation's ancestors and before "
        + "its own document: it overrides every ancestor and loses only to the annotation's own content. It appears as "
        + "template:{typeCode} in compositions (explain_configuration) and is not checked against schemas.";

    public const string Level =
        "The schema level: \"type\" (every configuration of annotationType), \"annotation\" (the configuration of key) "
        + "or \"descendantType\" (the configurations of type annotationType below the ancestor key).";

    public const string SchemaType =
        "The annotation type code (rst, unt, sbt, usg, cnt, exe or uxe), for the levels type and descendantType.";

    public const string SchemaKey = "The annotation key, for the levels annotation and descendantType (the ancestor).";

    public const string DiffVersion = "The version to compare, from the matching list_*_versions tool.";
}
