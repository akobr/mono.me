namespace _42.Platform.Storyteller.Accessing.Model;

public record IssuedCertificate(
    byte[] Pkcs12,
    string Password,
    string Thumbprint,
    string SerialNumber,
    DateTimeOffset NotBefore,
    DateTimeOffset NotAfter);
