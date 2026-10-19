namespace _42.Platform.Storyteller.Api.V1;

// The most bytes a handler reads of its JSON body (RequestBodyExtensions.ReadJsonBodyAsync); a larger body is a 413.
// Where the domain bounds a body, its limit is above the largest one, serialized as a System.Text.Json client does by
// default (a quote, '<' or a non-ASCII character as \uXXXX: 6 bytes). Where it does not, as for a view description,
// the labels and values of annotations or a whole import, the limit is a cap above what the clients send, and refuses
// some bodies the route took before. All are below the 30 MB request limit of Kestrel, which answers 500, even for a
// chunked body. The 32 KiB of the token endpoint (FederationHttp.MaxTokenRequestBytes) is for a form body: it would
// refuse valid issuers, rules, key/values and annotations.
internal static class RequestBodyLimits
{
    // Names, keys, roles, flags and short lists: an account, an access point, a permission, a member role, an
    // invitation, a shared certificate, a machine authentication policy, a key/value policy, a key rotation, a
    // recipient update and a key/value export request.
    public const int Control = 8 * 1024;

    // A view and its description, which has no limit of its own (the admin UI takes 500 characters).
    public const int View = 64 * 1024;

    // A client encryption recipient and its public key; the PEM of an RSA-16384 key is about 13 KiB.
    public const int Recipient = 64 * 1024;

    // A key/value bulk request: at most 100 keys of 128 characters, or a prefix.
    public const int KeyValueBulk = 64 * 1024;

    // One key/value write: a value of at most 24,576 bytes, or a client-encrypted one of 49,152 characters.
    public const int KeyValueWrite = 512 * 1024;

    // At most 100 annotation keys or lines; a key has at most about 1,000 characters (the Cosmos id).
    public const int AnnotationKeys = 1024 * 1024;

    // A trusted issuer: its JWKS of at most 16 KiB, the claims it requires and its JWKS URI.
    public const int FederatedIssuer = 1024 * 1024;

    // At most 10 federation rules of up to 10 claims with 10 values of 512 characters each: the rules of a machine,
    // or a new machine with them.
    public const int FederationRules = 4 * 1024 * 1024;

    // One annotation, which is at most a Cosmos item (2 MB).
    public const int Annotation = 4 * 1024 * 1024;

    // At most 100 annotations. Their labels and values have no limit of their own; one annotation is at most a Cosmos
    // item (2 MB).
    public const int Annotations = 16 * 1024 * 1024;

    // A key/value import of at most 1,000 items: just below the request limit of Kestrel, so that a larger chunked body
    // is refused here rather than by Kestrel's 500. 1,000 escaped values at their limit would not fit below it anyway.
    public const int KeyValueImport = 28 * 1024 * 1024;
}
