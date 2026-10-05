namespace _42.Platform.Storyteller.Accessing;

// The Storyteller scopes each machine access scope grants. Machine credentials carry these as roles.
public static class MachineAccessScopes
{
    public static IReadOnlyList<string> Get(MachineAccessScope scope)
    {
        return scope switch
        {
            MachineAccessScope.AnnotationRead => ["Annotation.Read"],
            MachineAccessScope.AnnotationReadWrite => ["Annotation.Read", "Annotation.ReadWrite"],
            MachineAccessScope.ConfigurationRead => ["Configuration.Read"],
            MachineAccessScope.ConfigurationReadWrite => ["Configuration.Read", "Configuration.ReadWrite"],
            MachineAccessScope.DefaultRead => ["Default.Read", "Annotation.Read", "Configuration.Read"],
            MachineAccessScope.DefaultReadWrite => ["Default.Read", "Default.ReadWrite", "Annotation.Read", "Annotation.ReadWrite", "Configuration.Read", "Configuration.ReadWrite"],
            _ => [],
        };
    }
}
