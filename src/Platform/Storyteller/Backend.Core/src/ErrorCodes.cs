namespace _42.Platform.Storyteller;

// Stable values of ErrorResponse.ErrorCode. Clients branch on them, so existing values never change.
public static class ErrorCodes
{
    public const string AccessDenied = "AccessDenied";
    public const string NotFound = "NotFound";
    public const string Conflict = "Conflict";
    public const string InvalidName = "InvalidName";
    public const string ViewExists = "ViewExists";

    // A JSON body larger than its route reads (413).
    public const string BodyTooLarge = "BodyTooLarge";

    // A machine restricted to an annotation addressed another annotation, or an operation of the whole project.
    public const string AnnotationRestricted = "AnnotationRestricted";

    // A token acts for the user through an actor (the MCP server) whose restrictions refuse the operation, such as
    // NoSecretReveal for a request of secret values.
    public const string ActorRestricted = "ActorRestricted";

    public const string AccountExists = "AccountExists";
    public const string AccessPointExists = "AccessPointExists";
    public const string LastOwner = "LastOwner";
    public const string ElevatedRole = "ElevatedRole";
    public const string SelfRoleChange = "SelfRoleChange";
    public const string MemberNotFound = "MemberNotFound";

    public const string InvitationExists = "InvitationExists";
    public const string InvitationNotPending = "InvitationNotPending";
    public const string InvitationExpired = "InvitationExpired";
    public const string EmailNotVerified = "EmailNotVerified";
    public const string EmailMismatch = "EmailMismatch";

    // Workload identity federation: trusted issuers of a project and federation rules of its machines.
    public const string InvalidFederation = "InvalidFederation";
    public const string MachineNotFederated = "MachineNotFederated";
    public const string FederatedIssuerInUse = "FederatedIssuerInUse";

    public const string PatchInvalid = "PatchInvalid";
    public const string PatchTestFailed = "PatchTestFailed";

    public const string InvalidAnnotation = "InvalidAnnotation";
    public const string InvalidKey = "InvalidKey";
    public const string AnnotationDeleted = "AnnotationDeleted";
    public const string AnnotationNotDeleted = "AnnotationNotDeleted";

    public const string ValueRequired = "ValueRequired";
    public const string ValueTooLarge = "ValueTooLarge";
    public const string InvalidContentType = "InvalidContentType";
    public const string ProtectionRequired = "ProtectionRequired";
    public const string ImportInvalid = "ImportInvalid";
    public const string SecretValueMissing = "SecretValueMissing";
    public const string NotSupportedForProtection = "NotSupportedForProtection";
    public const string ReprotectNotPossible = "ReprotectNotPossible";
    public const string KeyValueNotFound = "KeyValueNotFound";
    public const string KeyValueBusy = "KeyValueBusy";
    public const string KeyValueVersionMismatch = "KeyValueVersionMismatch";
    public const string KeyValueIntegrity = "KeyValueIntegrity";
    public const string ProtectionNotAllowed = "ProtectionNotAllowed";
    public const string ProtectionNotConfigured = "ProtectionNotConfigured";
    public const string ProtectionInUse = "ProtectionInUse";
    public const string KeyEncryptionUnavailable = "KeyEncryptionUnavailable";
    public const string CustomerKeyUnavailable = "CustomerKeyUnavailable";
    public const string CustomerKeyInvalid = "CustomerKeyInvalid";
    public const string ByokNotConfigured = "ByokNotConfigured";
    public const string MtlsRequired = "MtlsRequired";
    public const string HttpsRequired = "HttpsRequired";
    public const string InvalidClientEncryptedValue = "InvalidClientEncryptedValue";
    public const string UnknownRecipient = "UnknownRecipient";
    public const string RecipientExists = "RecipientExists";
    public const string RecipientNotFound = "RecipientNotFound";
    public const string RecipientInvalid = "RecipientInvalid";
    public const string InvalidSecretReference = "InvalidSecretReference";
    public const string PolicyVersionMismatch = "PolicyVersionMismatch";
    public const string InvalidQuery = "InvalidQuery";
    public const string AnnotationInactive = "AnnotationInactive";

    // Export and import of a view: errors of the requests.
    public const string TransferNotReady = "TransferNotReady";
    public const string TransferLimit = "TransferLimit";
    public const string InvalidArchive = "InvalidArchive";
    public const string ArchiveTooLarge = "ArchiveTooLarge";

    // Export and import of a view: outcomes of single files in an import (SchemaViolation is a warning on a
    // forced write).
    public const string AnnotationNotFound = "AnnotationNotFound";
    public const string EmptyDocument = "EmptyDocument";
    public const string InvalidDocument = "InvalidDocument";
    public const string DuplicateFile = "DuplicateFile";
    public const string SchemaViolation = "SchemaViolation";
    public const string DocumentExists = "DocumentExists";
    public const string DependencyFailed = "DependencyFailed";
    public const string InvalidSchema = "InvalidSchema";

    // Export and import of a view: warnings of written documents.
    public const string AncestorCreated = "AncestorCreated";
    public const string AnnotationRecreated = "AnnotationRecreated";
}
