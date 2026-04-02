using System;

namespace _42.Platform.Storyteller.Sdk;

public interface ISdkConfiguration
{
    string BaseUrl { get; }

    Func<string>? AccessTokenFactory { get; }
}

public class SdkConfiguration : ISdkConfiguration
{
    public string BaseUrl { get; set; } = "https://api.42.com";

    public Func<string>? AccessTokenFactory { get; set; }
}
