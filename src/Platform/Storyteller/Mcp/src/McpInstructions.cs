namespace _42.Platform.Storyteller.Mcp;

// The server instructions every MCP client receives at initialization.
public static class McpInstructions
{
    public const string Text =
        """
        This server manages the 2S Platform (Storyteller) as the signed-in person; the person's role on each project decides what succeeds.

        Addressing:
        - Everything belongs to an organization, a project and a view. list_projects returns keys "organization.project"; view defaults to "default".
        - Annotation keys are {typeCode}.{name}[.{name}...]: rst.{responsibility}, sbt.{subject}, unt.{responsibility}.{unit}, usg.{subject}.{responsibility}, cnt.{subject}.{context}, exe.{subject}.{responsibility}.{context}, uxe.{subject}.{responsibility}.{context}.{unit}.

        Configuration:
        - A configuration inherits along the annotation hierarchy and through the view's templates; explain_configuration shows where each value comes from.
        - Writes address the annotation's own stored document, not the effective one: read with get_configuration form "own" before you edit, and never write back an effective document.

        Lists return pages: pass the ContinuationToken of a result to get the next page.

        Safety:
        - Ask the person before a destructive call and before any tool that returns secrets.
        - Machine credentials returned by create_machine or reset_machine are shown once; tell the person to copy them from the tool result instead of retyping them.
        """;
}
