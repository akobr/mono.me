namespace _42.Platform.Storyteller.Configuring;

// The kinds of Binding.BindingDependency that the built-in bindings record. The source of a key/value pair or an
// annotation is the view of the configuration being resolved; its key is the normalized key or the annotation key.
public static class BindingDependencyKinds
{
    public const string KeyValue = "kv";

    public const string Annotation = "annotation";

    // The source is the name the vault is registered under, the key the secret name.
    public const string KeyVault = "vault";
}
