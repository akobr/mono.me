using System.Security.Claims;
using System.Security.Cryptography;
using System.Text.Json;
using _42.Platform.Storyteller.Accessing;
using _42.Platform.Storyteller.Accessing.Federation;
using _42.Platform.Storyteller.Accessing.Model;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;

namespace _42.Platform.Storyteller.Access.Certificates.IntegrationTests;

// The whole exchange on Cosmos DB: a platform token of a trusted issuer (a stored JWKS, so nothing is fetched) for a
// Storyteller token, which the bearer validator then accepts; the policy and the machine are read on every exchange.
[Collection(IntegrationTestCollection.Name)]
public class FederatedExchangeTests(CosmosFixture fixture)
{
    private const string Organization = "fedorg";
    private const string Project = "fedexchange";
    private const string PlatformIssuer = "https://ci.example/issuer";
    private const string PlatformAudience = "https://storyteller.example/fedexchange";
    private const string StorytellerIssuer = "urn:storyteller:test.platform";

    [Fact]
    public async Task PlatformToken_IsExchanged_UntilTheKindIsDisabledOrTheMachineDeleted()
    {
        using var platformKey = RSA.Create(2048);
        var accessService = fixture.Services.GetRequiredService<IAccessService>();
        var policyStore = fixture.Services.GetRequiredService<IMachineAuthenticationPolicyStore>();
        var exchange = fixture.Services.GetRequiredService<IFederatedTokenExchange>();
        await EnsureProjectAsync(accessService);
        await policyStore.SetAsync(Organization, Project, FederatedPolicy());
        await fixture.Services.GetRequiredService<IFederatedIssuerStore>().SetAsync(Organization, Project, new FederatedIssuer
        {
            Name = "ci",
            Issuer = PlatformIssuer,
            KeySource = FederatedIssuerKeySource.Jwks,
            Jwks = Jwks(platformKey),
        });
        var machine = await accessService.CreateMachineAccessAsync(new MachineAccessCreate
        {
            Organization = Organization,
            Project = Project,
            Scopes = [MachineScope.ConfigurationRead],
            CredentialKind = MachineCredentialKind.Federated,
            FederationRules = [new FederationRule { Issuer = "ci", Audience = PlatformAudience, Subject = "ci:job:deploy" }],
        });
        var request = new FederatedTokenExchangeRequest
        {
            Organization = Organization,
            Project = Project,
            MachineId = machine.Id,
            SubjectToken = Sign(platformKey, "ci:job:deploy"),
            SubjectTokenType = OAuthTokenExchange.TokenTypes.Jwt,
        };

        var exchanged = await exchange.ExchangeAsync(request);

        exchanged.Succeeded.ShouldBeTrue(exchanged.ErrorDescription);
        exchanged.Scope.ShouldBe("Configuration.Read");
        exchanged.IssuedTokenType.ShouldBe(OAuthTokenExchange.TokenTypes.AccessToken);
        var validator = fixture.Services.GetServices<IMachineTokenValidator>().Single(candidate => candidate.OwnsIssuer(StorytellerIssuer));
        var identity = await validator.ValidateAsync(exchanged.AccessToken!);
        identity.ShouldNotBeNull();
        identity.IsMachine.ShouldBeTrue();
        identity.MachineId.ShouldBe(machine.Id);
        identity.CredentialKind.ShouldBe(MachineCredentialKind.Federated);
        identity.Organization.ShouldBe(Organization);
        identity.Project.ShouldBe(Project);
        identity.Claims.Where(claim => claim.Type == "roles").Select(claim => claim.Value).ShouldBe(["Configuration.Read"]);

        // A token of another subject matches no rule.
        (await exchange.ExchangeAsync(request with { SubjectToken = Sign(platformKey, "ci:job:other") })).Error.ShouldBe(OAuthTokenExchange.Errors.InvalidRequest);

        // The exchange reads the policy of the access point itself, so disabling the kind applies at once.
        await policyStore.SetAsync(Organization, Project, new MachineAuthenticationPolicy { EnabledCredentialKinds = [MachineCredentialKind.ApiKey] });
        (await exchange.ExchangeAsync(request)).ErrorDescription.ShouldBe(FederatedTokenExchangeResult.NotAcceptedDescription);

        await policyStore.SetAsync(Organization, Project, FederatedPolicy());
        (await exchange.ExchangeAsync(request)).Succeeded.ShouldBeTrue();

        (await accessService.DeleteMachineAccessAsync(Organization, Project, machine.Id)).ShouldBeTrue();
        (await exchange.ExchangeAsync(request)).ErrorDescription.ShouldBe(FederatedTokenExchangeResult.NotAcceptedDescription);
    }

    private static MachineAuthenticationPolicy FederatedPolicy()
    {
        return new MachineAuthenticationPolicy
        {
            EnabledCredentialKinds = [MachineCredentialKind.ApiKey, MachineCredentialKind.Federated],
            CredentialKind = MachineCredentialKind.ApiKey,
        };
    }

    private static string Jwks(RSA key)
    {
        var parameters = key.ExportParameters(includePrivateParameters: false);
        return JsonSerializer.Serialize(new
        {
            keys = new[]
            {
                new { kty = "RSA", kid = "ci-1", use = "sig", n = Base64UrlEncoder.Encode(parameters.Modulus), e = Base64UrlEncoder.Encode(parameters.Exponent) },
            },
        });
    }

    private static string Sign(RSA key, string subject)
    {
        var now = DateTime.UtcNow;
        return new JsonWebTokenHandler().CreateToken(new SecurityTokenDescriptor
        {
            Issuer = PlatformIssuer,
            Audience = PlatformAudience,
            Subject = new ClaimsIdentity([new Claim("sub", subject)]),
            IssuedAt = now,
            NotBefore = now,
            Expires = now.AddMinutes(10),
            SigningCredentials = new SigningCredentials(new RsaSecurityKey(key) { KeyId = "ci-1" }, SecurityAlgorithms.RsaSha256),
        });
    }

    private static async Task EnsureProjectAsync(IAccessService accessService)
    {
        try
        {
            await accessService.CreateAccessPointAsync(new AccessPointCreate { Organization = Organization, Project = Project, OwnerId = "test-owner" });
        }
        catch (ConflictException)
        {
            // Already exists.
        }
    }
}
