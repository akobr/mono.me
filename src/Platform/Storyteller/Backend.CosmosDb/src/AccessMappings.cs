using _42.Platform.Storyteller.Entities.Access;

namespace _42.Platform.Storyteller;

internal static class AccessMappings
{
    public static Account ToAccount(this AccountEntity e) => new()
    {
        Id = e.Id,
        UserName = e.UserName,
        Name = e.Name,
        AccessMap = e.AccessMap,
    };

    public static AccountEntity ToEntity(this Account m) => new()
    {
        Id = m.Id,
        UserName = m.UserName,
        Name = m.Name,
        AccessMap = new(m.AccessMap),
    };

    public static AccessPoint ToAccessPoint(this AccessPointEntity e) => new()
    {
        Key = e.Key,
        AccessMap = e.AccessMap,
        MachineAuthentication = e.MachineAuthentication,
    };

    public static AccessPointEntity ToEntity(this AccessPoint m) => new()
    {
        Key = m.Key,
        AccessMap = new(m.AccessMap),
        MachineAuthentication = m.MachineAuthentication,
    };

    public static MachineAccess ToMachineAccess(this MachineAccessEntity e) => new()
    {
        Id = e.Id,
        ObjectId = e.ObjectId,
        AccessKey = e.AccessKey,
        Scope = e.Scope,
        AnnotationKey = e.AnnotationKey,
        CredentialKind = e.CredentialKind,
        CertificateThumbprint = e.CertificateThumbprint,
        LastRenewalAt = e.LastRenewalAt,
    };
}
