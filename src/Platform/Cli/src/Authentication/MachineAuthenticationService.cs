using System;
using System.Collections.Generic;
using System.IO.Abstractions;
using System.Linq;
using System.Net.Http.Headers;
using System.Security.Cryptography.X509Certificates;
using System.Threading;
using System.Threading.Tasks;
using _42.Platform.Storyteller.Sdk.Credentials;

namespace _42.Platform.Cli.Authentication;

// Non-interactive sign-in of a machine with the credentials in STORYTELLER_* environment variables: an API key, a
// client certificate, both, client credentials, a token file, or the platform token a federated machine exchanges.
// The credential is the SDK's (StorytellerCredentialVariables.CreateCredential): it requests, caches and renews the
// tokens. Nothing is stored on disk; a token lives in memory for the process.
public sealed class MachineAuthenticationService : IAuthenticationService, IMachineSignIn, IDisposable
{
    private const string ApiKeyScheme = "ApiKey";
    private const string BearerScheme = "Bearer";

    // The token factory and the renewal handler pass no cancellation, and the SDK honours any Retry-After. A token request
    // therefore ends here, retries included, as a single request did with the 100-second default of HttpClient.
    private static readonly TimeSpan DefaultTokenDeadline = TimeSpan.FromSeconds(100);

    private readonly IFileSystem _fileSystem;
    private readonly TimeProvider _time;
    private readonly MachineCredentialHooks _hooks;
    private readonly Lazy<LoadedCredential> _credential;
    private readonly object _bindingLock = new();

    // Copies replaced by a binding to another target; a call may still use one, so they are disposed with the service.
    private readonly List<StorytellerCredential> _replaced = [];
    private volatile BoundCredential? _bound;
    private volatile ExchangeTarget? _target;

    public MachineAuthenticationService(MachineSignInSettings settings, IFileSystem fileSystem)
        : this(settings, fileSystem, TimeProvider.System, MachineCredentialHooks.None)
    {
    }

    internal MachineAuthenticationService(
        MachineSignInSettings settings,
        IFileSystem fileSystem,
        TimeProvider time,
        MachineCredentialHooks hooks)
    {
        Settings = settings;
        _fileSystem = fileSystem;
        _time = time;
        _hooks = hooks;
        _credential = new Lazy<LoadedCredential>(Load);
    }

    public MachineSignInSettings Settings { get; }

    public bool IsConfigured => Settings.IsConfigured;

    public MachineIdentity GetIdentity()
    {
        return GetCredential().Identity;
    }

    public MachineIdentity? TryGetIdentity()
    {
        return IsConfigured && _credential.Value.Failure is null ? _credential.Value.Identity : null;
    }

    public X509Certificate2? TryGetCertificate()
    {
        return IsConfigured && _credential.Value.Failure is null ? _credential.Value.Credential.ClientCertificate : null;
    }

    public async Task<MachineIdentity> SignInAsync(CancellationToken cancellationToken = default)
    {
        var credential = GetCredential();

        return credential.Identity.Kind switch
        {
            MachineSignInKind.ClientCredentials => credential.Identity with { TokenExpiresAt = JwtExpiry.Read(await GetTokenAsync(credential, null, null, cancellationToken)) },
            MachineSignInKind.TokenFile => credential.Identity with { TokenExpiresAt = await ReadTokenFileExpiryAsync(credential, cancellationToken) },
            MachineSignInKind.Federated => await SignInFederatedAsync(credential, cancellationToken),
            _ => credential.Identity,
        };
    }

    public async Task<SignedInUser?> GetSignedInUserAsync(CancellationToken cancellationToken = default)
    {
        if (!IsConfigured)
        {
            return null;
        }

        var identity = await SignInAsync(cancellationToken);
        var id = identity.MachineId ?? "machine";
        var name = identity.MachineId is null ? "machine" : $"machine {identity.MachineId}";
        var project = identity.Organization is null ? string.Empty : $" ({identity.Organization}.{identity.Project})";
        return new SignedInUser(id, name + project, null);
    }

    public async Task<string?> GetAccessTokenAsync(CancellationToken cancellationToken = default)
    {
        if (!IsConfigured)
        {
            return null;
        }

        var credential = GetCredential();

        // One read: a command may bind another target meanwhile.
        var target = _target;

        // Only for the target a command bound: sform account ca and the user-only commands must not exchange, nor ask
        // GitHub for a token, just because STORYTELLER_PROJECT or STORYTELLER_KEY is set.
        if (credential.Identity.Kind == MachineSignInKind.Federated && target is null)
        {
            return null;
        }

        var authorization = await GetAuthorizationAsync(credential, target, null, cancellationToken);

        // The certificate goes in the TLS handshake (or the development header); no Authorization header.
        return authorization switch
        {
            null => null,
            { Scheme: ApiKeyScheme } => $"{ApiKeyScheme} {authorization.Parameter}",
            _ => authorization.Parameter,
        };
    }

    // Kinds other than Federated never read it.
    public void BindTarget(string organization, string project)
    {
        _target = new ExchangeTarget(organization, project);
    }

    public async Task<string?> RenewAccessTokenAsync(string rejectedToken, CancellationToken cancellationToken = default)
    {
        if (!IsConfigured)
        {
            return null;
        }

        var credential = GetCredential();
        var target = _target;

        if (!credential.Credential.CanRenew || (credential.Identity.Kind == MachineSignInKind.Federated && target is null))
        {
            return null;
        }

        return await GetTokenAsync(credential, target, rejectedToken, cancellationToken);
    }

    public Task<SignedInUser> LoginWithDeviceCodeAsync(
        Func<DeviceCodePrompt, Task> onPrompt,
        Func<IReadOnlyList<OrganizationChoice>, Task<string>>? selectOrganization = null,
        CancellationToken cancellationToken = default)
    {
        throw new AuthenticationException(
            AuthenticationFailureReason.Configuration,
            $"sform signs in with the machine credentials in {string.Join(", ", Settings.CredentialVariables)}; unset them to sign in as a user.");
    }

    // Nothing is stored, so there is nothing to forget.
    public Task LogoutAsync(CancellationToken cancellationToken = default)
    {
        return Task.CompletedTask;
    }

    public void Dispose()
    {
        lock (_bindingLock)
        {
            _bound?.Credential.Dispose();
            _replaced.ForEach(credential => credential.Dispose());
            _replaced.Clear();
        }

        if (_credential.IsValueCreated)
        {
            _credential.Value.Credential?.Dispose();
            _credential.Value.Credential?.ClientCertificate?.Dispose();
        }
    }

    private static AuthenticationException Invalid(string message)
    {
        return new AuthenticationException(AuthenticationFailureReason.Configuration, message);
    }

    private static void EnsureCertificateMatchesKey(
        MachineSignInKind kind,
        MachineCertificateIdentity? certificate,
        MachineApiKey? key,
        string certificateVariable)
    {
        if (certificate is { IsShared: true } && kind != MachineSignInKind.CertificateAndApiKey)
        {
            throw Invalid($"A shared certificate signs in only together with its machine's API key; set {StorytellerVariables.API_KEY} too.");
        }

        if (certificate is null || key is null)
        {
            return;
        }

        var sameProject = string.Equals(certificate.Organization, key.Organization, StringComparison.OrdinalIgnoreCase)
            && string.Equals(certificate.Project, key.Project, StringComparison.OrdinalIgnoreCase);
        var sameMachine = certificate.IsShared || string.Equals(certificate.MachineId, key.MachineId, StringComparison.OrdinalIgnoreCase);

        if (!sameProject)
        {
            throw Invalid(
                $"The certificate in {certificateVariable} belongs to {certificate.Organization}.{certificate.Project} but the API key to {key.Organization}.{key.Project}.");
        }

        if (!sameMachine)
        {
            throw Invalid(
                $"The certificate in {certificateVariable} belongs to machine {certificate.MachineId} but the API key to machine {key.MachineId}.");
        }
    }

    private LoadedCredential GetCredential()
    {
        if (!IsConfigured)
        {
            throw Invalid("No machine sign-in variable is set.");
        }

        var credential = _credential.Value;
        var failure = credential.Failure;

        // A new exception on every call: the cached one keeps the reason and the message.
        return failure is null
            ? credential
            : throw new AuthenticationException(failure.Reason, failure.Message, failure.InnerException);
    }

    private LoadedCredential Load()
    {
        StorytellerCredential? credential = null;

        try
        {
            var kind = Settings.GetKind();
            var variables = Settings.GetCredential();
            var endpoint = Settings.GetEndpoint();

            // Loads the certificate.
            credential = CredentialFailures.Guard(() => variables.CreateCredential(_fileSystem, _time));

            // Messages say where a platform token from GitHub Actions came from; names only, never values.
            IReadOnlyList<string> names = variables.SubjectTokenSource == StorytellerSubjectTokenSource.GitHubActions
                ? [.. variables.Names, StorytellerVariables.ACTIONS_ID_TOKEN_REQUEST_URL, StorytellerVariables.ACTIONS_ID_TOKEN_REQUEST_TOKEN]
                : variables.Names;
            var key = kind is MachineSignInKind.ApiKey or MachineSignInKind.CertificateAndApiKey
                ? MachineApiKey.TryParseMachine(variables.ApiKeyMachine)
                : null;
            MachineCertificateIdentity? certificateIdentity = null;

            if (credential.ClientCertificate is { } certificate)
            {
                certificateIdentity = MachineCertificate.ReadIdentity(certificate);

                // As set: the deprecated name when the canonical one is unset.
                var certificateVariable = variables.Names.Contains(StorytellerVariables.CLIENT_CERTIFICATE)
                    ? StorytellerVariables.CLIENT_CERTIFICATE
                    : StorytellerVariables.CERTIFICATE_FILE;
                EnsureCertificateMatchesKey(kind, certificateIdentity, key, certificateVariable);
            }

            var identity = kind switch
            {
                MachineSignInKind.ApiKey => new MachineIdentity(kind, names, key?.MachineId, key?.Organization, key?.Project),
                MachineSignInKind.Certificate => new MachineIdentity(kind, names, certificateIdentity?.MachineId, certificateIdentity?.Organization, certificateIdentity?.Project),

                // A machine certificate names the machine; with a shared one the key does.
                MachineSignInKind.CertificateAndApiKey when certificateIdentity is { IsShared: false } =>
                    new MachineIdentity(kind, names, certificateIdentity.MachineId, certificateIdentity.Organization, certificateIdentity.Project),
                MachineSignInKind.CertificateAndApiKey => new MachineIdentity(
                    kind,
                    names,
                    key?.MachineId,
                    key?.Organization ?? certificateIdentity?.Organization,
                    key?.Project ?? certificateIdentity?.Project),

                // The credential names no project; a federated machine gets its project from the command.
                MachineSignInKind.ClientCredentials or MachineSignInKind.Federated => new MachineIdentity(kind, names, variables.ClientId, null, null),
                _ => new MachineIdentity(kind, names, null, null, null),
            };

            return new LoadedCredential(identity, credential, endpoint, null);
        }
        catch (AuthenticationException exception)
        {
            credential?.ClientCertificate?.Dispose();
            return new LoadedCredential(null!, null!, null!, exception);
        }
    }

    // A token file that has visibly expired fails the sign-in here instead of with a 401 on the next call.
    private async Task<DateTimeOffset?> ReadTokenFileExpiryAsync(LoadedCredential credential, CancellationToken cancellationToken)
    {
        var expiresAt = JwtExpiry.Read(await GetTokenAsync(credential, null, null, cancellationToken));

        return expiresAt is null || expiresAt > _time.GetUtcNow()
            ? expiresAt
            : throw new AuthenticationException(
                AuthenticationFailureReason.Expired,
                $"The token in {StorytellerVariables.TOKEN_FILE} expired at {expiresAt:O}.");
    }

    // A command binds its target; without one, STORYTELLER_PROJECT, and then the project of STORYTELLER_KEY, names the
    // project to sign in to.
    private async Task<MachineIdentity> SignInFederatedAsync(LoadedCredential credential, CancellationToken cancellationToken)
    {
        var target = _target
            ?? (MachineSignInSettings.TrySplitProject(Settings.Project, out var organization, out var project) ? new ExchangeTarget(organization, project) : null)
            ?? (Settings.Key is { } key ? new ExchangeTarget(key.OrganizationName, key.ProjectName) : null)
            ?? (Settings.IsKeyInvalid ? throw Settings.InvalidKey() : (ExchangeTarget?)null)
            ?? throw Invalid(
                $"No project is set for the machine: set {TargetVariables.PROJECT}=<organization>.<project>, set {StorytellerVariables.KEY} to a full key of the project, or pass -p.");
        var token = await GetTokenAsync(credential, target, null, cancellationToken);

        return credential.Identity with { Organization = target.Organization, Project = target.Project, TokenExpiresAt = JwtExpiry.Read(token) };
    }

    // The bearer token of a credential that has one.
    private async Task<string> GetTokenAsync(LoadedCredential credential, ExchangeTarget? target, string? rejectedToken, CancellationToken cancellationToken)
    {
        var rejected = rejectedToken is null ? null : new AuthenticationHeaderValue(BearerScheme, rejectedToken);
        var authorization = await GetAuthorizationAsync(credential, target, rejected, cancellationToken);

        return authorization?.Parameter
            ?? throw new InvalidOperationException($"The machine credential of the kind {credential.Identity.Kind} has no token.");
    }

    private async Task<AuthenticationHeaderValue?> GetAuthorizationAsync(
        LoadedCredential credential,
        ExchangeTarget? target,
        AuthenticationHeaderValue? rejected,
        CancellationToken cancellationToken)
    {
        var bound = Bind(credential, target);
        var deadline = _hooks.TokenDeadline ?? DefaultTokenDeadline;
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(deadline);

        try
        {
            return await CredentialFailures.GuardAsync(() => bound.GetAuthorizationAsync(rejected, timeout.Token));
        }
        catch (OperationCanceledException) when (timeout.IsCancellationRequested && !cancellationToken.IsCancellationRequested)
        {
            throw new AuthenticationException(
                AuthenticationFailureReason.ServiceError,
                $"The machine got no token within {deadline.TotalSeconds:0} seconds, retries and Retry-After waits included: the token endpoint, the sign-in discovery or the GitHub Actions runner did not answer, or kept failing.");
        }
    }

    // One bound copy at a time: it holds the token. A federated machine is bound to the organization and project of its
    // target, and bound again when the target changes; every other kind is bound once, without a target.
    private StorytellerCredential Bind(LoadedCredential credential, ExchangeTarget? target)
    {
        var key = credential.Identity.Kind == MachineSignInKind.Federated ? target?.Key : null;

        if (_bound is { } bound && bound.Key == key)
        {
            return bound.Credential;
        }

        lock (_bindingLock)
        {
            if (_bound is { } current && current.Key == key)
            {
                return current.Credential;
            }

            var binding = new StorytellerCredentialBinding(credential.Endpoint)
            {
                Organization = key is null ? null : target!.Organization,
                Project = key is null ? null : target!.Project,
                Resilience = _hooks.Resilience,
            };
            var copy = CredentialFailures.Guard(() =>
            {
                var boundCopy = credential.Credential.Bind(binding);
                return _hooks.Decorate?.Invoke(boundCopy) ?? boundCopy;
            });

            if (_bound is { } replaced && !ReferenceEquals(replaced.Credential, credential.Credential))
            {
                _replaced.Add(replaced.Credential);
            }

            _bound = new BoundCredential(key, copy);
            return copy;
        }
    }

    // The project of a federated exchange; its token endpoint belongs to that project.
    private sealed record ExchangeTarget(string Organization, string Project)
    {
        public string Key => $"{Organization}.{Project}";
    }

    // Key is the organization.project of a federated copy, null for every other kind.
    private sealed record BoundCredential(string? Key, StorytellerCredential Credential);

    private sealed record LoadedCredential(
        MachineIdentity Identity,
        StorytellerCredential Credential,
        Uri Endpoint,
        AuthenticationException? Failure);
}
