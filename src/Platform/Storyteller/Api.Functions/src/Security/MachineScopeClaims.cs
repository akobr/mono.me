namespace _42.Platform.Storyteller.Api.Security;

public static class MachineScopeClaims
{
    public static IReadOnlyList<string> GetRoleClaimsForScope(MachineAccessScope scope)
    {
        return scope switch
        {
            MachineAccessScope.AnnotationRead => [Scopes.Annotation.Read],
            MachineAccessScope.AnnotationReadWrite => [Scopes.Annotation.Read, Scopes.Annotation.Write],
            MachineAccessScope.ConfigurationRead => [Scopes.Configuration.Read],
            MachineAccessScope.ConfigurationReadWrite => [Scopes.Configuration.Read, Scopes.Configuration.Write],
            MachineAccessScope.DefaultRead => [Scopes.Default.Read, Scopes.Annotation.Read, Scopes.Configuration.Read],
            MachineAccessScope.DefaultReadWrite => [Scopes.Default.Read, Scopes.Default.Write, Scopes.Annotation.Read, Scopes.Annotation.Write, Scopes.Configuration.Read, Scopes.Configuration.Write],
            _ => [],
        };
    }
}
