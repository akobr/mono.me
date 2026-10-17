using Microsoft.Extensions.Options;

namespace _42.Platform.Storyteller.Accessing;

public sealed class FederationOptionsValidator : IValidateOptions<FederationOptions>
{
    private const string Section = FederationOptions.SectionName;

    public ValidateOptionsResult Validate(string? name, FederationOptions options)
    {
        var failures = new List<string>();

        if (string.IsNullOrWhiteSpace(options.Issuer))
        {
            failures.Add($"{Section}:Issuer is required.");
        }

        if (string.IsNullOrWhiteSpace(options.Audience))
        {
            failures.Add($"{Section}:Audience is required.");
        }

        // The token endpoint URL handed to workloads is built from it, so it is an absolute base without query or fragment.
        if (!string.IsNullOrEmpty(options.PublicBaseUrl)
            && (!Uri.TryCreate(options.PublicBaseUrl, UriKind.Absolute, out var baseUrl)
                || (baseUrl.Scheme != Uri.UriSchemeHttps && baseUrl.Scheme != Uri.UriSchemeHttp)
                || options.PublicBaseUrl.Contains('?')
                || options.PublicBaseUrl.Contains('#')))
        {
            failures.Add($"{Section}:PublicBaseUrl must be an absolute http(s) URL without query or fragment.");
        }

        if (options.TokenLifetimeMinutes is < FederationOptions.MinTokenLifetimeMinutes or > FederationOptions.MaxTokenLifetimeMinutes)
        {
            failures.Add($"{Section}:TokenLifetimeMinutes must be between {FederationOptions.MinTokenLifetimeMinutes} and {FederationOptions.MaxTokenLifetimeMinutes}.");
        }

        if (options.ClockSkewSeconds is < 0 or > FederationOptions.MaxClockSkewSeconds)
        {
            failures.Add($"{Section}:ClockSkewSeconds must be between 0 and {FederationOptions.MaxClockSkewSeconds}.");
        }

        // The token endpoint reads at most 32 KiB of form body, so a larger limit could never apply.
        if (options.MaxSubjectTokenBytes is < FederationOptions.MinSubjectTokenBytes or > FederationOptions.MaxSubjectTokenBytesLimit)
        {
            failures.Add($"{Section}:MaxSubjectTokenBytes must be between {FederationOptions.MinSubjectTokenBytes} and {FederationOptions.MaxSubjectTokenBytesLimit}.");
        }

        if (options.MetadataRefreshMinutes is < 1 or > FederationOptions.MaxMetadataRefreshMinutes)
        {
            failures.Add($"{Section}:MetadataRefreshMinutes must be between 1 and {FederationOptions.MaxMetadataRefreshMinutes}.");
        }

        return failures.Count > 0
            ? ValidateOptionsResult.Fail(failures)
            : ValidateOptionsResult.Success;
    }
}
