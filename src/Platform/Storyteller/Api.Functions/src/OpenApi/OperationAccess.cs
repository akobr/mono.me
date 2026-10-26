using System.Collections.Frozen;

namespace _42.Platform.Storyteller.Api.OpenApi;

// The callers an operation admits, for its security requirements in the document; the handlers enforce them. An operation
// with the manual scheme that is in neither set is for users and machines; one without any requirement is anonymous.
internal static class OperationAccess
{
    // Spec 7.4: these refuse every machine, whatever its credential carries (EnsureUser, CheckUserAccessTo*Async).
    public static readonly FrozenSet<string> UserOnly = FrozenSet.ToFrozenSet(
    [
        Definitions.RouteIds.Access.GetAccount,
        Definitions.RouteIds.Access.CreateAccount,
        Definitions.RouteIds.Access.GetAccessPoints,
        Definitions.RouteIds.Access.GetAccessPoint,
        Definitions.RouteIds.Access.UpdateAccessPoint,
        Definitions.RouteIds.Access.GetProjects,
        Definitions.RouteIds.Access.CreateAccessPoint,
        Definitions.RouteIds.Access.GrantUserAccess,
        Definitions.RouteIds.Access.RevokeUserAccess,
        Definitions.RouteIds.Access.GetMembers,
        Definitions.RouteIds.Access.SetMemberRole,
        Definitions.RouteIds.Access.RemoveMember,
        Definitions.RouteIds.Access.GetInvitations,
        Definitions.RouteIds.Access.CreateInvitation,
        Definitions.RouteIds.Access.ResendInvitation,
        Definitions.RouteIds.Access.RevokeInvitation,
        Definitions.RouteIds.Access.GetMyInvitations,
        Definitions.RouteIds.Access.AcceptInvitation,
        Definitions.RouteIds.Access.DeclineInvitation,
        Definitions.RouteIds.Access.GetMachineAccesses,
        Definitions.RouteIds.Access.GetMachineAccess,
        Definitions.RouteIds.Access.CreateMachineAccess,
        Definitions.RouteIds.Access.ResetMachineAccess,
        Definitions.RouteIds.Access.DeleteMachineAccess,
        Definitions.RouteIds.Access.GetMachineAuthentication,
        Definitions.RouteIds.Access.SetMachineAuthentication,
        Definitions.RouteIds.Access.GetSharedCertificates,
        Definitions.RouteIds.Access.IssueSharedCertificate,
        Definitions.RouteIds.Access.RevokeSharedCertificate,
        Definitions.RouteIds.Access.GetFederatedIssuers,
        Definitions.RouteIds.Access.GetFederatedIssuer,
        Definitions.RouteIds.Access.SetFederatedIssuer,
        Definitions.RouteIds.Access.DeleteFederatedIssuer,
        Definitions.RouteIds.Access.SetMachineFederationRules,
        Definitions.RouteIds.Annotations.PurgeAnnotation,
        Definitions.RouteIds.Views.CreateView,
        Definitions.RouteIds.Views.UpdateView,
        Definitions.RouteIds.KeyValues.PurgeKeyValues,
        Definitions.RouteIds.KeyValues.SetKeyValuePolicy,
        Definitions.RouteIds.KeyValues.GetByokSetup,
        Definitions.RouteIds.KeyValues.VerifyByok,
        Definitions.RouteIds.KeyValues.RotateKeyValueKey,
        Definitions.RouteIds.KeyValues.AddRecipient,
        Definitions.RouteIds.KeyValues.UpdateRecipient,
        Definitions.RouteIds.KeyValues.RemoveRecipient,
        Definitions.RouteIds.KeyValues.GetKeyValueAudit,
        Definitions.RouteIds.KeyValues.GetKeyValueUsage,
    ],
    StringComparer.Ordinal);

    // A machine renews its certificate with the certificate itself, and with its API key when its kind needs both.
    public static readonly FrozenSet<string> CertificateRenewal = FrozenSet.ToFrozenSet([Definitions.RouteIds.Access.RenewMachineCertificate], StringComparer.Ordinal);
}
