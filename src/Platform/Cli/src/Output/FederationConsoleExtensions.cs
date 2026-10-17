using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using _42.CLI.Toolkit.Output;
using _42.Platform.Storyteller.Sdk;

namespace _42.Platform.Cli.Output;

// Workload identity federation: the rules of a federated machine, the token exchange its workload runs,
// and the trusted issuers of a project.
public static class FederationConsoleExtensions
{
    public const string TokenExchangeGrantType = "urn:ietf:params:oauth:grant-type:token-exchange";
    public const string JwtTokenType = "urn:ietf:params:oauth:token-type:jwt";

    // The server sets TokenEndpoint on every Federated machine; the placeholder only keeps the output readable.
    public static string GetTokenEndpoint(MachineAccess machine, string organization, string project)
    {
        return string.IsNullOrWhiteSpace(machine.TokenEndpoint)
            ? $"<API base URL>/v1/{organization}/{project}/access/token"
            : machine.TokenEndpoint;
    }

    // The machine id (the client_id of an exchange), the token endpoint and the rules.
    public static void WriteFederatedMachine(this IExtendedConsole @this, MachineAccess machine, string tokenEndpoint)
    {
        @this.WriteLine("Machine ID:     ", (machine.Id ?? string.Empty).ThemedHighlight(@this.Theme));
        @this.WriteLine("Token endpoint: ", tokenEndpoint.ThemedHighlight(@this.Theme));
        @this.WriteLine();
        @this.WriteFederationRules(machine.FederationRules);
    }

    public static void WriteFederationRules(this IExtendedConsole @this, IEnumerable<FederationRule>? rules)
    {
        var ruleList = rules?.ToList() ?? [];

        if (ruleList.Count < 1)
        {
            @this.WriteLine("No federation rules.".ThemedLowlight(@this.Theme));
            return;
        }

        for (var index = 0; index < ruleList.Count; index++)
        {
            var rule = ruleList[index];
            @this.WriteLine($"Rule {index + 1}:");
            @this.WriteLine($"  Issuer:   {rule.Issuer}");
            @this.WriteLine($"  Audience: {rule.Audience}");
            @this.WriteLine($"  Subject:  {rule.Subject}");

            foreach (var claim in FormatClaims(rule.Claims))
            {
                @this.WriteLine($"  Claim:    {claim}");
            }
        }
    }

    // The RFC 8693 exchange of a platform token for a Storyteller token, as curl.
    public static void WriteTokenExchange(this IExtendedConsole @this, string tokenEndpoint, string machineId)
    {
        @this.WriteLine("Exchange a token of the workload's platform (with a rule's audience) for a Storyteller token, sent as Authorization: Bearer <access_token>:");
        @this.WriteLine(
            $"  curl -s -X POST {tokenEndpoint} -d grant_type={TokenExchangeGrantType} -d client_id={machineId} -d subject_token_type={JwtTokenType} --data-urlencode subject_token=$PLATFORM_TOKEN"
                .ThemedLowlight(@this.Theme));
    }

    public static void WriteFederatedIssuers(this IExtendedConsole @this, ICollection<FederatedIssuer> issuers)
    {
        if (issuers.Count < 1)
        {
            @this.WriteLine("No trusted issuers, yet.".ThemedLowlight(@this.Theme));
            return;
        }

        @this.WriteTable(
            issuers,
            issuer => new[]
            {
                issuer.Name,
                issuer.Issuer,
                DescribeKeySource(issuer),
                DescribeMaxLifetime(issuer.MaxTokenLifetimeSeconds),
                string.Join(", ", FormatClaims(issuer.RequiredClaims)),
            },
            new[] { "Name", "Issuer", "Keys", "MaxLifetime", "RequiredClaims" });
    }

    public static void WriteFederatedIssuer(this IExtendedConsole @this, FederatedIssuer issuer)
    {
        @this.WriteLine($"  Issuer:         {issuer.Issuer}");
        @this.WriteLine($"  Keys:           {DescribeKeySource(issuer)}");
        @this.WriteLine($"  Max lifetime:   {DescribeMaxLifetime(issuer.MaxTokenLifetimeSeconds)}");

        foreach (var claim in FormatClaims(issuer.RequiredClaims))
        {
            @this.WriteLine($"  Required claim: {claim}");
        }

        if (!string.IsNullOrWhiteSpace(issuer.Description))
        {
            @this.WriteLine($"  Description:    {issuer.Description}");
        }
    }

    // One key=value per accepted value, the form --claim and --require-claim take.
    public static IEnumerable<string> FormatClaims(IDictionary<string, IReadOnlyList_String>? claims)
    {
        return (claims ?? new Dictionary<string, IReadOnlyList_String>())
            .SelectMany(claim => (claim.Value ?? []).Select(value => $"{claim.Key}={value}"));
    }

    private static string DescribeKeySource(FederatedIssuer issuer)
    {
        return issuer.KeySource switch
        {
            FederatedIssuerKeySource.JwksUri => $"JwksUri {issuer.JwksUri}",
            FederatedIssuerKeySource.Jwks => "Jwks (stored key set)",
            _ => "Discovery",
        };
    }

    private static string DescribeMaxLifetime(int? seconds)
    {
        return seconds.HasValue
            ? $"{seconds.Value.ToString(CultureInfo.InvariantCulture)} s"
            : "3600 s (default)";
    }
}
