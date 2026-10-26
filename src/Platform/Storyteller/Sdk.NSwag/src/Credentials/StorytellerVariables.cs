using System.Collections.Frozen;
using System.Collections.Generic;

namespace _42.Platform.Storyteller.Sdk.Credentials;

// The STORYTELLER_* names of the shared integration contract, the deprecated sform names that are still read when the
// canonical name is unset, and the GitHub Actions names a federated machine can take its platform token from.
public static class StorytellerVariables
{
    public const string ENDPOINT = "STORYTELLER_ENDPOINT";

    // Deprecated: ENDPOINT.
    public const string API_URL = "STORYTELLER_API_URL";

    public const string KEY = "STORYTELLER_KEY";

    public const string API_KEY = "STORYTELLER_API_KEY";
    public const string CLIENT_CERTIFICATE = "STORYTELLER_CLIENT_CERTIFICATE";
    public const string CLIENT_CERTIFICATE_PASSWORD = "STORYTELLER_CLIENT_CERTIFICATE_PASSWORD";

    // Deprecated: CLIENT_CERTIFICATE.
    public const string CERTIFICATE_FILE = "STORYTELLER_CERTIFICATE_FILE";

    // Deprecated: CLIENT_CERTIFICATE_PASSWORD.
    public const string CERTIFICATE_PASSWORD = "STORYTELLER_CERTIFICATE_PASSWORD";

    public const string CLIENT_ID = "STORYTELLER_CLIENT_ID";
    public const string CLIENT_SECRET = "STORYTELLER_CLIENT_SECRET";
    public const string TOKEN_URL = "STORYTELLER_TOKEN_URL";
    public const string TOKEN_SCOPE = "STORYTELLER_TOKEN_SCOPE";

    public const string TOKEN_FILE = "STORYTELLER_TOKEN_FILE";

    // Deprecated: TOKEN_FILE, of a federated machine only (see Deprecated).
    public const string SUBJECT_TOKEN_FILE = "STORYTELLER_SUBJECT_TOKEN_FILE";

    public const string SUBJECT_TOKEN_TYPE = "STORYTELLER_SUBJECT_TOKEN_TYPE";

    // Set by GitHub Actions in a job with permissions: id-token: write. Not credential variables: alone they configure
    // no machine; they only supply the platform token of one that STORYTELLER_CLIENT_ID already makes federated.
    public const string ACTIONS_ID_TOKEN_REQUEST_URL = "ACTIONS_ID_TOKEN_REQUEST_URL";
    public const string ACTIONS_ID_TOKEN_REQUEST_TOKEN = "ACTIONS_ID_TOKEN_REQUEST_TOKEN";

    // Every name that makes a credential, deprecated ones included, in the order they are read; not ENDPOINT, API_URL,
    // KEY or the GitHub Actions names.
    public static IReadOnlyList<string> Credentials { get; } =
    [
        API_KEY,
        CLIENT_CERTIFICATE,
        CERTIFICATE_FILE,
        CLIENT_CERTIFICATE_PASSWORD,
        CERTIFICATE_PASSWORD,
        CLIENT_ID,
        CLIENT_SECRET,
        TOKEN_URL,
        TOKEN_SCOPE,
        TOKEN_FILE,
        SUBJECT_TOKEN_FILE,
        SUBJECT_TOKEN_TYPE,
    ];

    // A deprecated name and the canonical name that replaces it; read only when the canonical name is unset, and both set
    // to different values is an error. SUBJECT_TOKEN_FILE is no plain alias: set, it always makes the credential
    // federated (never a bearer token file) and needs CLIENT_ID; only then is it compared with TOKEN_FILE.
    public static IReadOnlyDictionary<string, string> Deprecated { get; } = new Dictionary<string, string>
    {
        [API_URL] = ENDPOINT,
        [CERTIFICATE_FILE] = CLIENT_CERTIFICATE,
        [CERTIFICATE_PASSWORD] = CLIENT_CERTIFICATE_PASSWORD,
        [SUBJECT_TOKEN_FILE] = TOKEN_FILE,
    }.ToFrozenDictionary();
}
