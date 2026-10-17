using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.IO;
using System.IO.Abstractions;
using System.Linq;
using _42.Platform.Storyteller.Sdk;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace _42.Platform.Cli.Commands.MachineAccess;

// The federation rules of a Federated machine as the CLI takes them: one rule from --issuer, --audience, --subject
// and any --claim, or a JSON array of rules in the API's FederationRule shape from --rules-file. Only the options are
// checked here; the server validates the rules themselves (issuer names, audiences, subjects, claim keys).
internal sealed record FederationRuleOptions(
    string? Issuer,
    string? Audience,
    string? Subject,
    IReadOnlyList<string>? Claims,
    string? RulesFile)
{
    public const string Usage = "--issuer, --audience and --subject (with any --claim key=value), or --rules-file <path>";

    // An Azure managed identity's token carries the aud of the app registration that stands for Storyteller, and the
    // registration's token version decides which form (docs/Platform/Storyteller/authentication.md, Azure managed identity).
    public const string AzureAudienceHelp = "For Azure, the aud of tokens for the Storyteller app registration: <appId> with requestedAccessTokenVersion 2, api://<appId> with 1.";

    private static readonly JsonSerializer RuleSerializer = JsonSerializer.Create(new JsonSerializerSettings
    {
        // Otherwise FederationRule keeps an unknown property in AdditionalProperties and sends it on.
        MissingMemberHandling = MissingMemberHandling.Error,
        DateParseHandling = DateParseHandling.None,
    });

    public bool HasRuleOptions => Issuer is not null || Audience is not null || Subject is not null || Claims is { Count: > 0 };

    public bool IsGiven => HasRuleOptions || RulesFile is not null;

    public bool TryRead(
        IFileSystem fileSystem,
        [NotNullWhen(true)] out List<FederationRule>? rules,
        [NotNullWhen(false)] out string? error)
    {
        rules = null;

        if (!IsGiven)
        {
            error = $"A federated machine needs federation rules: {Usage}.";
            return false;
        }

        if (RulesFile is not null && HasRuleOptions)
        {
            error = "Give either --rules-file or --issuer, --audience and --subject (with --claim), not both.";
            return false;
        }

        if (RulesFile is not null)
        {
            return TryReadFile(fileSystem, RulesFile, out rules, out error);
        }

        var missing = new[] { (Name: "--issuer", Value: Issuer), (Name: "--audience", Value: Audience), (Name: "--subject", Value: Subject) }
            .Where(option => string.IsNullOrWhiteSpace(option.Value))
            .Select(option => option.Name)
            .ToList();

        if (missing.Count > 0)
        {
            error = $"A rule needs --issuer (the name of a trusted issuer), --audience and --subject; missing {string.Join(", ", missing)}.";
            return false;
        }

        if (!ClaimOptions.TryParse(Claims, "--claim", out var claims, out error))
        {
            return false;
        }

        rules =
        [
            new FederationRule
            {
                Issuer = Issuer!.Trim(),
                Audience = Audience!.Trim(),
                Subject = Subject!.Trim(),
                Claims = claims,
            },
        ];
        return true;
    }

    private static bool TryReadFile(
        IFileSystem fileSystem,
        string path,
        [NotNullWhen(true)] out List<FederationRule>? rules,
        [NotNullWhen(false)] out string? error)
    {
        rules = null;

        if (!fileSystem.File.Exists(path))
        {
            error = $"The rules file {path} does not exist.";
            return false;
        }

        try
        {
            if (Parse(fileSystem.File.ReadAllText(path)) is not JArray array)
            {
                error = $"The rules file {path} has to hold a JSON array of rules: [ {{ \"Issuer\": ..., \"Audience\": ..., \"Subject\": ..., \"Claims\": {{ \"key\": [ \"value\" ] }} }} ].";
                return false;
            }

            if (array.Any(item => item is not JObject))
            {
                error = $"Every rule in the rules file {path} has to be a JSON object.";
                return false;
            }

            // An unknown property (a misspelled "Claim") fails here; the server would drop it without a word.
            rules = array.ToObject<List<FederationRule>>(RuleSerializer) ?? [];

            if (FindValueThatIsNoString(array, path) is { } notString)
            {
                rules = null;
                error = notString;
                return false;
            }
        }
        catch (JsonException exception)
        {
            error = $"The rules file {path} is not a JSON array of rules: {exception.Message}";
            return false;
        }

        if (rules.Count < 1)
        {
            rules = null;
            error = $"The rules file {path} holds no rule.";
            return false;
        }

        error = null;
        return true;
    }

    private static JToken Parse(string json)
    {
        // Strings stay as written: by default an ISO date becomes a DateTime and returns in the current culture's format.
        using var reader = new JsonTextReader(new StringReader(json)) { DateParseHandling = DateParseHandling.None };
        var token = JToken.ReadFrom(reader);

        // As in JToken.Parse: anything after the array but a comment throws.
        while (reader.Read())
        {
        }

        return token;
    }

    // The serializer would turn true into "True" and 1.0 into "1": a value the API refuses would become a rule that
    // never matches, since the server compares claims ordinally. Runs after ToObject, so every property here is one
    // of FederationRule's.
    private static string? FindValueThatIsNoString(JArray rules, string path)
    {
        for (var index = 0; index < rules.Count; index++)
        {
            foreach (var property in ((JObject)rules[index]).Properties())
            {
                if (!string.Equals(property.Name, nameof(FederationRule.Claims), StringComparison.OrdinalIgnoreCase))
                {
                    if (property.Value.Type != JTokenType.String)
                    {
                        return $"Rule {index + 1} of the rules file {path}: {property.Name} has to be a string, not {property.Value.ToString(Formatting.None)}.";
                    }

                    continue;
                }

                if (property.Value is not JObject claims)
                {
                    continue;
                }

                foreach (var claim in claims.Properties())
                {
                    if (claim.Value is not JArray values || values.Any(value => value.Type != JTokenType.String))
                    {
                        return $"Rule {index + 1} of the rules file {path}: the claim {claim.Name} takes an array of strings, for example [ \"74\" ], not {claim.Value.ToString(Formatting.None)}.";
                    }
                }
            }
        }

        return null;
    }
}
