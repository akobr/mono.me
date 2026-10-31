using System;

namespace _42.Platform.Storyteller.Configuring;

// A schema referred to a URL or a file (a $ref that does not start with '#'); the API never fetches or reads one.
internal sealed class ExternalSchemaReferenceException(string reference)
    : Exception($"The schema refers to the external document '{ConfigurationSchemaChecker.Shorten(reference)}'; only references inside the schema are resolved.")
{
    public string Reference { get; } = reference;
}
