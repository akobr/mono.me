using _42.Platform.Storyteller.AspireHosting;
using Aspire.Hosting.ApplicationModel;

// The namespace of Aspire's own integrations, so an AppHost needs no using for builder.AddStoryteller(…).
namespace Aspire.Hosting;

public static class StorytellerResourceBuilderExtensions
{
    private const string ResourceType = "Storyteller";

    // A Storyteller API at a literal URL, checked with the SDK's rule: an absolute https URL without user info, a query
    // or a fragment, or http to localhost or a loopback address. The process checks again.
    public static IResourceBuilder<StorytellerResource> AddStoryteller(this IDistributedApplicationBuilder builder, string name, string endpoint)
    {
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentNullException.ThrowIfNull(endpoint);
        EndpointRule.Require(endpoint, nameof(endpoint));

        return builder.AddStoryteller(name, ReferenceExpression.Create($"{endpoint}"));
    }

    // The endpoint from a parameter, resolved at start; a manifest holds its reference.
    public static IResourceBuilder<StorytellerResource> AddStoryteller(this IDistributedApplicationBuilder builder, string name, IResourceBuilder<ParameterResource> endpoint)
    {
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentNullException.ThrowIfNull(endpoint);

        return builder.AddStoryteller(name, ReferenceExpression.Create($"{endpoint.Resource}"));
    }

    // Any expression, for example the endpoint of the platform's own Api.Functions project with its /api path.
    public static IResourceBuilder<StorytellerResource> AddStoryteller(this IDistributedApplicationBuilder builder, string name, ReferenceExpression endpoint)
    {
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        ArgumentNullException.ThrowIfNull(endpoint);

        // Running from the start, so WaitFor(storyteller) proceeds; the dashboard shows the URL, or the expression's
        // text for a parameter or an endpoint reference, never a resolved parameter.
        return builder.AddResource(new StorytellerResource(name, endpoint))
            .ExcludeFromManifest()
            .WithInitialState(new CustomResourceSnapshot
            {
                ResourceType = ResourceType,
                State = KnownResourceStates.Running,
                Properties = [new ResourcePropertySnapshot(CustomResourceKnownProperties.Source, endpoint.ValueExpression)],
            });
    }

    // $"{api.GetEndpoint("http")}/api": the handler, so a non-constant interpolated string does not bind to string and
    // its references stay references.
    public static IResourceBuilder<StorytellerResource> AddStoryteller(this IDistributedApplicationBuilder builder, string name, in ReferenceExpression.ExpressionInterpolatedStringHandler endpoint)
    {
        return builder.AddStoryteller(name, ReferenceExpression.Create(in endpoint));
    }

    // The organization, project and view that prefix the annotation keys of WithStorytellerKey and WithExecution.
    public static IResourceBuilder<StorytellerResource> WithView(this IResourceBuilder<StorytellerResource> builder, string organization, string project, string view = "default")
    {
        ArgumentNullException.ThrowIfNull(builder);
        RequireName(organization, nameof(organization));
        RequireName(project, nameof(project));
        RequireName(view, nameof(view));

        builder.Resource.SetView(organization, project, view);
        return builder;
    }

    // STORYTELLER_API_KEY. Combines with a client certificate only.
    public static IResourceBuilder<StorytellerResource> WithApiKey(this IResourceBuilder<StorytellerResource> builder, IResourceBuilder<ParameterResource> apiKey)
    {
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentNullException.ThrowIfNull(apiKey);

        builder.Resource.AddApiKey(apiKey.Resource);
        return builder;
    }

    // STORYTELLER_CLIENT_ID and STORYTELLER_CLIENT_SECRET, with STORYTELLER_TOKEN_URL and STORYTELLER_TOKEN_SCOPE when
    // set. Alone: the SDK refuses client credentials with an API key or a certificate.
    public static IResourceBuilder<StorytellerResource> WithClientCredentials(
        this IResourceBuilder<StorytellerResource> builder,
        IResourceBuilder<ParameterResource> clientId,
        IResourceBuilder<ParameterResource> clientSecret,
        string? tokenUrl = null,
        string? tokenScope = null)
    {
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentNullException.ThrowIfNull(clientId);
        ArgumentNullException.ThrowIfNull(clientSecret);
        RequireTextOrNull(tokenUrl, nameof(tokenUrl));
        RequireTextOrNull(tokenScope, nameof(tokenScope));

        builder.Resource.AddClientCredentials(clientId.Resource, clientSecret.Resource, tokenUrl, tokenScope);
        return builder;
    }

    // STORYTELLER_CLIENT_CERTIFICATE, and STORYTELLER_CLIENT_CERTIFICATE_PASSWORD with a password. The path is made
    // absolute against the AppHost directory now, as a process started elsewhere would not find a relative one.
    public static IResourceBuilder<StorytellerResource> WithClientCertificate(this IResourceBuilder<StorytellerResource> builder, string path, IResourceBuilder<ParameterResource>? password = null)
    {
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentException.ThrowIfNullOrWhiteSpace(path);

        builder.Resource.AddClientCertificate(Path.GetFullPath(path, builder.ApplicationBuilder.AppHostDirectory), password?.Resource);
        return builder;
    }

    // The STORYTELLER_* variables of the resource, for a project, an executable or a container: STORYTELLER_ENDPOINT and
    // the credential's, the contract every SDK and sform read. A second call replaces the first: one set per process.
    public static IResourceBuilder<TDestination> WithReference<TDestination>(this IResourceBuilder<TDestination> builder, IResourceBuilder<StorytellerResource> storyteller)
        where TDestination : IResourceWithEnvironment
    {
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentNullException.ThrowIfNull(storyteller);

        var destination = builder.Resource;
        var resource = storyteller.Resource;
        RequireNoCertificateInContainer(destination, resource);

        var previous = destination.Annotations.OfType<StorytellerReferenceAnnotation>().LastOrDefault();

        if (previous?.Relationship is { } previousRelationship)
        {
            destination.Annotations.Remove(previousRelationship);
        }

        builder.WithReferenceRelationship(resource);
        var relationship = destination.Annotations.OfType<ResourceRelationshipAnnotation>().LastOrDefault(annotation => ReferenceEquals(annotation.Resource, resource));
        builder.WithAnnotation(new StorytellerReferenceAnnotation(resource, relationship), ResourceAnnotationMutationBehavior.Replace);

        // One callback per destination, which writes the resource of the last call.
        if (previous is null)
        {
            builder.WithEnvironment(context => WriteReference(context, destination));
        }

        return builder;
    }

    // STORYTELLER_KEY: a full key as written, or an annotation key prefixed in the callback with the view of the
    // Storyteller resource the destination references, so the order of the calls does not matter. The last call wins.
    // A full key needs no WithReference.
    public static IResourceBuilder<TDestination> WithStorytellerKey<TDestination>(this IResourceBuilder<TDestination> builder, string key)
        where TDestination : IResourceWithEnvironment
    {
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentException.ThrowIfNullOrWhiteSpace(key);

        if (!(StorytellerKeyRule.IsFull(key) ? StorytellerKeyRule.IsFullKey(key) : StorytellerKeyRule.IsAnnotationKey(key)))
        {
            throw new ArgumentException(
                $"'{key}' is not a Storyteller key: set a full key @{{organization}}.{{project}}.{{view}}.{{annotation key}}, or an annotation key such as exe.{{subject}}.{{responsibility}}.{{context}} with the segments of its type.",
                nameof(key));
        }

        var destination = builder.Resource;
        var hasKey = destination.Annotations.OfType<StorytellerKeyAnnotation>().Any();
        builder.WithAnnotation(new StorytellerKeyAnnotation(key), ResourceAnnotationMutationBehavior.Replace);

        // One callback per destination, which writes the key of the last call, so an earlier key never fails a start.
        if (!hasKey)
        {
            builder.WithEnvironment(context => WriteKey(context, destination));
        }

        return builder;
    }

    // STORYTELLER_KEY of the execution exe.{subject}.{responsibility}.{context}, in the view of the referenced resource.
    public static IResourceBuilder<TDestination> WithExecution<TDestination>(this IResourceBuilder<TDestination> builder, string subject, string responsibility, string context)
        where TDestination : IResourceWithEnvironment
    {
        ArgumentNullException.ThrowIfNull(builder);
        RequireName(subject, nameof(subject));
        RequireName(responsibility, nameof(responsibility));
        RequireName(context, nameof(context));

        return builder.WithStorytellerKey($"exe.{subject}.{responsibility}.{context}");
    }

    private static void WriteReference(EnvironmentCallbackContext context, IResource destination)
    {
        var resource = destination.Annotations.OfType<StorytellerReferenceAnnotation>().Last().Resource;

        // A certificate added after WithReference.
        RequireNoCertificateInContainer(destination, resource);
        resource.WriteVariables(context.EnvironmentVariables);
    }

    // Throws from the callback, so Aspire fails the start of that resource and names it.
    private static void WriteKey(EnvironmentCallbackContext context, IResource destination)
    {
        var key = destination.Annotations.OfType<StorytellerKeyAnnotation>().Last().Key;

        if (StorytellerKeyRule.IsFull(key))
        {
            context.EnvironmentVariables[StorytellerVariableNames.KEY] = key;
            return;
        }

        var resource = destination.Annotations.OfType<StorytellerReferenceAnnotation>().LastOrDefault()?.Resource;

        if (resource is not { Organization: { } organization, Project: { } project, View: { } view })
        {
            throw new InvalidOperationException(
                $"The Storyteller key of '{destination.Name}' is the annotation key {key}, which needs a view: call WithView on the Storyteller resource and WithReference to it, or set a full key @{{organization}}.{{project}}.{{view}}.{{annotation key}}.");
        }

        context.EnvironmentVariables[StorytellerVariableNames.KEY] = $"@{organization}.{project}.{view}.{key}";
    }

    // A certificate path on the host does not exist in a container.
    private static void RequireNoCertificateInContainer(IResource destination, StorytellerResource resource)
    {
        if (destination is ContainerResource && resource.CertificatePath is not null)
        {
            throw new InvalidOperationException(
                $"The Storyteller resource '{resource.Name}' has a client certificate, a path on the AppHost's machine that the container '{destination.Name}' does not have. Reference a Storyteller resource with an API key or client credentials from a container.");
        }
    }

    private static void RequireName(string name, string parameterName)
    {
        ArgumentNullException.ThrowIfNull(name, parameterName);

        if (!StorytellerKeyRule.IsName(name))
        {
            throw new ArgumentException("A name in a Storyteller key is one segment: not empty and without a '.'.", parameterName);
        }
    }

    private static void RequireTextOrNull(string? value, string parameterName)
    {
        if (value is not null && string.IsNullOrWhiteSpace(value))
        {
            throw new ArgumentException("The value is empty; leave it null to keep it unset.", parameterName);
        }
    }
}
