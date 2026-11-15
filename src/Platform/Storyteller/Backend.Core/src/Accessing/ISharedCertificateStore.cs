using System.Collections.Generic;
using _42.Platform.Storyteller.Accessing.Model;

namespace _42.Platform.Storyteller.Accessing;

public interface ISharedCertificateStore
{
    Task<IReadOnlyList<SharedCertificate>> ListAsync(string organization, string project);

    Task<SharedCertificate?> GetByThumbprintAsync(string organization, string project, string thumbprint);

    // Stores a new certificate, with the caller's access audit record.
    Task StoreAsync(string organization, string project, SharedCertificate certificate, AccessCaller caller);

    // False when the certificate doesn't exist; revoking a revoked one changes and records nothing.
    Task<bool> RevokeAsync(string organization, string project, string thumbprint, AccessCaller caller);

    Task<bool> LabelExistsAsync(string organization, string project, string label);
}
