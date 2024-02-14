using System;
using _42.Platform.Sdk.Client;

namespace _42.Platform.Sdk;

public class DynamicConfiguration : Configuration
{
    public Func<string> AccessTokenFactory { get; set; }

    public override string AccessToken
    {
        get => AccessTokenFactory();
        set => throw new NotSupportedException();
    }
}
