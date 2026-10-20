using _42.Platform.Storyteller.Accessing;
using _42.Platform.Storyteller.Accessing.Federation;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace _42.Platform.Storyteller;

// The RFC 8693 exchange of a platform token for a Storyteller token. The checks run cheapest first and the anonymous
// endpoint reads nothing for unknown projects, disabled kinds or untrusted issuers: token form, project policy
// (core container), trusted issuers (cached), the machine, the signature and rules, then the scope.
public class FederatedTokenExchangeService : IFederatedTokenExchange
{
    public const string ScopeExceedsMachineDescription = "The requested scope exceeds the scopes of the machine.";
    public const string SigningKeyUnavailableDescription = "The signing key of Storyteller tokens is not available.";

    private static readonly string[] SubjectTokenTypes =
    [
        OAuthTokenExchange.TokenTypes.Jwt,
        OAuthTokenExchange.TokenTypes.IdToken,
        OAuthTokenExchange.TokenTypes.AccessToken,
    ];

    private static readonly string[] RequestedTokenTypes =
    [
        OAuthTokenExchange.TokenTypes.AccessToken,
        OAuthTokenExchange.TokenTypes.Jwt,
    ];

    private readonly IAccessService _accessService;
    private readonly IFederatedIssuerStore _issuerStore;
    private readonly SubjectTokenVerifier _verifier;
    private readonly StorytellerTokenIssuer _tokenIssuer;
    private readonly IOptions<FederationOptions> _options;
    private readonly IOptions<MachineAuthenticationOptions> _machineOptions;
    private readonly ILogger<FederatedTokenExchangeService> _logger;

    public FederatedTokenExchangeService(
        IAccessService accessService,
        IFederatedIssuerStore issuerStore,
        SubjectTokenVerifier verifier,
        StorytellerTokenIssuer tokenIssuer,
        IOptions<FederationOptions> options,
        IOptions<MachineAuthenticationOptions> machineOptions,
        ILogger<FederatedTokenExchangeService> logger)
    {
        _accessService = accessService;
        _issuerStore = issuerStore;
        _verifier = verifier;
        _tokenIssuer = tokenIssuer;
        _options = options;
        _machineOptions = machineOptions;
        _logger = logger;
    }

    public async Task<FederatedTokenExchangeResult> ExchangeAsync(FederatedTokenExchangeRequest request, CancellationToken cancellationToken = default)
    {
        // 1. The endpoint checked the form; checked again so no other caller skips it.
        if (ValidateRequest(request) is { } invalid)
        {
            return FederatedTokenExchangeResult.Failure(OAuthTokenExchange.Errors.InvalidRequest, invalid);
        }

        var organization = request.Organization;
        var project = request.Project;
        var machineId = request.MachineId;

        // 2. The subject token's size, form and unverified issuer. Federated machine ids are GUIDs.
        if (!UnverifiedJwt.TryRead(request.SubjectToken, _options.Value.MaxSubjectTokenBytes, out var token, out var formError))
        {
            return Refuse(organization, project, machineId, formError);
        }

        if (!Guid.TryParse(machineId, out _))
        {
            return Refuse(organization, project, machineId, "the client_id is not a machine id");
        }

        // 3. The project exists and enables Federated machines; no organization container is touched before this.
        var accessPoint = await _accessService.GetAccessPointAsync($"{organization}.{project}");

        if (accessPoint is null)
        {
            return Refuse(organization, project, machineId, "the project doesn't exist");
        }

        var policy = MachineAuthenticationPolicies.Resolve(accessPoint.MachineAuthentication, _machineOptions.Value);

        if (!policy.IsEnabled(MachineCredentialKind.Federated))
        {
            return Refuse(organization, project, machineId, "the project doesn't enable Federated machines");
        }

        // 4. A trusted issuer with the token's `iss`, before the machine is read.
        var issuers = await _issuerStore.ListAsync(organization, project, cancellationToken);

        if (!issuers.Any(issuer => string.Equals(issuer.Issuer, token.Issuer, StringComparison.Ordinal)))
        {
            _logger.LogDebug(
                "A token exchange for the machine {MachineId} of {Organization}.{Project} is refused: the issuer is not trusted by the project",
                machineId,
                organization,
                project);
            return FederatedTokenExchangeResult.NotAccepted();
        }

        // 5. The machine, of kind Federated.
        var machine = await _accessService.GetMachineAccessAsync(organization, project, machineId);

        if (machine is null || machine.CredentialKind != MachineCredentialKind.Federated)
        {
            return Refuse(organization, project, machineId, machine is null ? "the machine doesn't exist" : $"the machine is {machine.CredentialKind}, not Federated");
        }

        // 6. The platform token against the machine's rules, then the scope.
        SubjectTokenMatch? match;

        try
        {
            match = await _verifier.VerifyAsync(request.SubjectToken, machine, issuers, organization, project, cancellationToken);
        }
        catch (BearerKeyRetrievalException exception)
        {
            _logger.LogWarning(exception, "A token exchange for the machine {MachineId} of {Organization}.{Project} is unavailable", machineId, organization, project);
            return FederatedTokenExchangeResult.Unavailable();
        }

        if (match is null)
        {
            return FederatedTokenExchangeResult.NotAccepted();
        }

        // The management scopes count as the machine's: a request may name them, and without scope the token gets them too.
        var machineScopes = MachineAccessScopes.Get(machine.Scope, machine.ManagementScopes);
        var requestedScopes = request.Scopes is { Count: > 0 } scopes ? scopes.Distinct(StringComparer.Ordinal).ToList() : machineScopes;

        // Least privilege for a single job: the token gets the requested subset only.
        if (requestedScopes.Count < 1 || requestedScopes.Any(scope => !machineScopes.Contains(scope, StringComparer.Ordinal)))
        {
            _logger.LogInformation(
                "A token exchange for the machine {MachineId} of {Organization}.{Project} asked for scopes beyond the machine's",
                machineId,
                organization,
                project);
            return FederatedTokenExchangeResult.Failure(OAuthTokenExchange.Errors.InvalidScope, ScopeExceedsMachineDescription);
        }

        // 7. The Storyteller token.
        StorytellerToken issued;

        try
        {
            issued = await _tokenIssuer.IssueAsync(new StorytellerTokenRequest
            {
                Organization = organization,
                Project = project,
                MachineId = machine.Id,
                Scopes = requestedScopes,
                AnnotationKey = machine.AnnotationKey,
                FederationIssuer = match.Issuer.Name,
                FederationSubject = match.Subject,
            });
        }
        catch (TokenSigningKeyUnavailableException exception)
        {
            _logger.LogError(exception, "No Storyteller token can be issued");
            return FederatedTokenExchangeResult.Unavailable(SigningKeyUnavailableDescription);
        }

        _logger.LogInformation(
            "The machine {MachineId} of {Organization}.{Project} exchanged a token of the trusted issuer {IssuerName} (rule {RuleNumber}, subject {Subject})",
            machine.Id,
            organization,
            project,
            match.Issuer.Name,
            match.RuleNumber,
            match.Subject);

        return FederatedTokenExchangeResult.Success(
            issued.AccessToken,
            request.RequestedTokenType,
            issued.ExpiresIn,
            string.Join(' ', requestedScopes));
    }

    // Null when the request has the shape the endpoint guarantees, else an invalid_request description.
    private static string? ValidateRequest(FederatedTokenExchangeRequest? request)
    {
        if (request is null)
        {
            return "The token request is missing.";
        }

        if (!IsKeySegment(request.Organization) || !IsKeySegment(request.Project))
        {
            return FederatedTokenExchangeResult.NotAcceptedDescription;
        }

        if (string.IsNullOrEmpty(request.MachineId))
        {
            return "The parameter client_id is missing.";
        }

        if (string.IsNullOrEmpty(request.SubjectToken))
        {
            return "The parameter subject_token is missing.";
        }

        if (!SubjectTokenTypes.Contains(request.SubjectTokenType, StringComparer.Ordinal))
        {
            return "The subject token type is not supported.";
        }

        if (!RequestedTokenTypes.Contains(request.RequestedTokenType, StringComparer.Ordinal))
        {
            return "The requested token type is not supported.";
        }

        if (request.Scopes is not null
            && request.Scopes.Any(scope => string.IsNullOrEmpty(scope) || scope.Any(character => char.IsWhiteSpace(character) || char.IsControl(character))))
        {
            return "The parameter scope is not valid.";
        }

        return null;
    }

    // Organization and project form the access point key "{organization}.{project}", so neither may hold a dot.
    private static bool IsKeySegment(string? value)
    {
        return !string.IsNullOrEmpty(value)
            && value.Length <= 128
            && !value.Any(character => character is '.' or '/' or '\\' or '?' or '#' || char.IsWhiteSpace(character) || char.IsControl(character));
    }

    private FederatedTokenExchangeResult Refuse(string organization, string project, string machineId, string reason)
    {
        _logger.LogInformation(
            "A token exchange for the machine {MachineId} of {Organization}.{Project} is refused: {Reason}",
            machineId,
            organization,
            project,
            reason);
        return FederatedTokenExchangeResult.NotAccepted();
    }
}
