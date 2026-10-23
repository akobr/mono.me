namespace _42.Platform.Storyteller.Sdk.Configuring;

public static class SecretReferenceResolver
{
    // One resolver over several: the first that accepts a value resolves it.
    public static ISecretReferenceResolver FirstOf(params ISecretReferenceResolver[] resolvers)
    {
        ArgumentNullException.ThrowIfNull(resolvers);

        if (Array.IndexOf(resolvers, null) >= 0)
        {
            throw new ArgumentException("A resolver is null.", nameof(resolvers));
        }

        // A copy: the caller's array may change later.
        return new FirstOfResolver([.. resolvers]);
    }

    private sealed class FirstOfResolver : ISecretReferenceResolver
    {
        private readonly ISecretReferenceResolver[] _resolvers;

        public FirstOfResolver(ISecretReferenceResolver[] resolvers)
        {
            _resolvers = resolvers;
        }

        public bool CanResolve(string value)
        {
            return Find(value) is not null;
        }

        public ValueTask<string> ResolveAsync(string value, CancellationToken cancellationToken)
        {
            var resolver = Find(value)
                ?? throw new SecretResolutionException("No resolver accepts the value; call CanResolve first.", false);

            return resolver.ResolveAsync(value, cancellationToken);
        }

        private ISecretReferenceResolver? Find(string value)
        {
            return Array.Find(_resolvers, resolver => resolver.CanResolve(value));
        }
    }
}
