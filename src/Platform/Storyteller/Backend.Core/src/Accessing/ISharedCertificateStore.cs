using System.Collections.Generic;
using _42.Platform.Storyteller.Accessing.Model;

namespace _42.Platform.Storyteller.Accessing;

public interface ISharedCertificateStore
{
    Task<IReadOnlyList<SharedCertificate>> ListAsync(string organization, string project);

    Task StoreAsync(string organization, string project, SharedCertificate certificate);

    Task<bool> RevokeAsync(string organization, string project, string thumbprint);

    Task<bool> LabelExistsAsync(string organization, string project, string label);
}
