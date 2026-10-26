using System;
using System.Collections.Generic;
using System.Linq;
using _42.Platform.Storyteller.Sdk.Credentials;

namespace _42.Platform.Cli.Authentication;

// The STORYTELLER_* variables of a non-interactive sign-in, read once per process with the shared credential contract
// of the SDK: the names and rules of the configuration provider, deprecated sform names included. Reading never throws;
// invalid variables are kept and reported by GetKind, on every call. Never exposes a secret.
public sealed class MachineSignInSettings
{
    private readonly StorytellerCredentialVariables? _credential;
    private readonly Exception? _credentialFailure;
    private readonly StorytellerAuthenticationException? _endpointFailure;

    private MachineSignInSettings(
        IReadOnlyList<string> credentialVariables,
        StorytellerCredentialVariables? credential,
        Exception? credentialFailure,
        string? endpoint,
        string endpointVariable,
        StorytellerAuthenticationException? endpointFailure)
    {
        CredentialVariables = credentialVariables;
        _credential = credential;
        _credentialFailure = credentialFailure;
        Endpoint = endpoint;
        EndpointVariable = endpointVariable;
        _endpointFailure = endpointFailure;

        IEnumerable<string> deprecated = credential?.DeprecatedNames ?? credentialVariables.Where(StorytellerVariables.Deprecated.ContainsKey);
        DeprecatedVariables = (endpoint is not null && endpointVariable == StorytellerVariables.API_URL ? deprecated.Prepend(StorytellerVariables.API_URL) : deprecated)
            .ToList()
            .AsReadOnly();
    }

    // The API base, /api included, as written: STORYTELLER_ENDPOINT, or the deprecated STORYTELLER_API_URL. Null when
    // neither is set, or when both are set to different values, which GetKind reports.
    public string? Endpoint { get; }

    // The variable the endpoint came from, for messages.
    public string EndpointVariable { get; }

    public string? Project { get; private init; }

    public string? View { get; private init; }

    // Any credential variable is set, valid or not: sform signs in as that machine and never as the stored user.
    // The GitHub Actions variables are not among them: a job with id-token: write may run sform as a user.
    public bool IsConfigured => CredentialVariables.Count > 0;

    // The names of the credential variables that are set, as named, never their values.
    public IReadOnlyList<string> CredentialVariables { get; }

    // The deprecated names in use: of the credential, and STORYTELLER_API_URL when it gave the endpoint.
    public IReadOnlyList<string> DeprecatedVariables { get; }

    public static MachineSignInSettings FromEnvironment(Func<string, string?> getVariable)
    {
        var target = new StorytellerVariableReader(getVariable);
        var project = target.Read(TargetVariables.PROJECT);
        var view = target.Read(TargetVariables.VIEW);

        var names = new StorytellerVariableReader(getVariable);
        var credentialVariables = StorytellerVariables.Credentials.Where(name => names.Read(name) is not null).ToList().AsReadOnly();

        string? endpoint = null;
        var endpointVariable = StorytellerVariables.ENDPOINT;
        StorytellerAuthenticationException? endpointFailure = null;

        try
        {
            endpoint = new StorytellerVariableReader(getVariable).ReadWithFallback(StorytellerVariables.ENDPOINT, StorytellerVariables.API_URL, out endpointVariable);
        }
        catch (StorytellerAuthenticationException exception)
        {
            endpointFailure = exception;
        }

        StorytellerCredentialVariables? credential = null;
        Exception? credentialFailure = null;

        if (credentialVariables.Count > 0)
        {
            try
            {
                credential = StorytellerCredentialVariables.Read(getVariable);
            }
            catch (Exception exception) when (exception is StorytellerAuthenticationException or ArgumentException)
            {
                credentialFailure = exception;
            }
        }

        return new MachineSignInSettings(credentialVariables, credential, credentialFailure, endpoint, endpointVariable, endpointFailure)
        {
            Project = project,
            View = view,
        };
    }

    // <organization>.<project>, split at the first dot like -p|--projectKey.
    public static bool TrySplitProject(string? projectKey, out string organization, out string project)
    {
        var separatorIndex = projectKey?.IndexOf('.') ?? -1;

        if (projectKey is null || separatorIndex <= 0 || separatorIndex == projectKey.Length - 1)
        {
            organization = string.Empty;
            project = string.Empty;
            return false;
        }

        organization = projectKey[..separatorIndex];
        project = projectKey[(separatorIndex + 1)..];
        return true;
    }

    // The credential, the endpoint and STORYTELLER_PROJECT are checked; throws AuthenticationException (Configuration),
    // a new one on every call.
    public MachineSignInKind GetKind()
    {
        var kind = GetCredential().Kind switch
        {
            StorytellerCredentialKind.ApiKey => MachineSignInKind.ApiKey,
            StorytellerCredentialKind.Certificate => MachineSignInKind.Certificate,
            StorytellerCredentialKind.CertificateAndApiKey => MachineSignInKind.CertificateAndApiKey,
            StorytellerCredentialKind.ClientCredentials => MachineSignInKind.ClientCredentials,
            StorytellerCredentialKind.TokenFile => MachineSignInKind.TokenFile,
            StorytellerCredentialKind.Federated => MachineSignInKind.Federated,
            var other => throw new InvalidOperationException($"No machine sign-in of the kind {other}."),
        };

        GetEndpoint();

        if (Project is not null && !TrySplitProject(Project, out _, out _))
        {
            throw Invalid($"{TargetVariables.PROJECT} must be <organization>.<project>.");
        }

        return kind;
    }

    // The checked credential variables; throws AuthenticationException (Configuration) as GetKind does.
    public StorytellerCredentialVariables GetCredential()
    {
        return _credentialFailure switch
        {
            StorytellerAuthenticationException failure => throw CredentialFailures.Map(failure),
            ArgumentException failure => throw CredentialFailures.Map(failure),
            _ => _credential ?? throw Invalid("No machine sign-in variable is set."),
        };
    }

    // Required for every kind; https, or http to localhost or a loopback address, with no user info, query or fragment.
    // Throws AuthenticationException (Configuration) that names the variable as it was set.
    public Uri GetEndpoint()
    {
        if (_endpointFailure is not null)
        {
            throw CredentialFailures.Map(_endpointFailure);
        }

        if (Endpoint is null)
        {
            throw Invalid($"Set {StorytellerVariables.ENDPOINT} to the API base URL, for example https://<host>/api.");
        }

        return CredentialFailures.Guard(() => StorytellerUrlRule.RequireEndpoint(Endpoint, EndpointVariable));
    }

    private static AuthenticationException Invalid(string message)
    {
        return new AuthenticationException(AuthenticationFailureReason.Configuration, message);
    }
}
