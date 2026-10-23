using System;
using Microsoft.Extensions.DependencyInjection;

namespace _42.Platform.Storyteller.Sdk.Credentials;

public static class StorytellerCredentialHttpClientBuilderExtensions
{
    // Leave ISdkConfiguration.AccessTokenFactory null on this path, or PrepareRequest sets Authorization too; the
    // client certificate still goes to SdkConfiguration.ClientCertificate. Bind a credential that needs a context first.
    public static IHttpClientBuilder AddStorytellerCredential(this IHttpClientBuilder builder, StorytellerCredential credential)
    {
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentNullException.ThrowIfNull(credential);

        return builder.AddHttpMessageHandler(() => new StorytellerCredentialHandler(credential));
    }
}
