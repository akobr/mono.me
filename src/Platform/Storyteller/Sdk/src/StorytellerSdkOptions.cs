using Microsoft.Extensions.Options;

namespace ApiSdk;

public class StorytellerSdkOptions : IOptions<StorytellerSdkOptions>
{
    public string TenantId { get; set; } = string.Empty;

    public string ClientId { get; set; } = string.Empty;

    public string[] AllowedHosts { get; set; } = [];

    StorytellerSdkOptions IOptions<StorytellerSdkOptions>.Value => this;
}
