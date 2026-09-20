using System.Security.Cryptography.X509Certificates;
using _42.Platform.Storyteller.Accessing.Model;

namespace _42.Platform.Storyteller.Accessing;

public interface IClientCertificateValidator
{
    Task<ClientCertificateValidationResult?> ValidateAsync(X509Certificate2 clientCertificate);
}
