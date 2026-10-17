using System.Security.Cryptography.X509Certificates;
using System.Threading;
using System.Threading.Tasks;

namespace _42.Platform.Cli.Authentication;

public interface IMachineSignIn
{
    // True when a credential variable is set; sform then never uses the stored user session.
    bool IsConfigured { get; }

    MachineSignInSettings Settings { get; }

    // What the credential tells about the machine, without network calls.
    // Throws AuthenticationException when the variables are invalid.
    MachineIdentity GetIdentity();

    // As GetIdentity, but null when no machine credential is set or it is invalid.
    MachineIdentity? TryGetIdentity();

    // The certificate of STORYTELLER_CERTIFICATE_FILE when it loads and has a private key, else null.
    X509Certificate2? TryGetCertificate();

    // Validates the variables; for client credentials and token files it also gets the token.
    // Throws AuthenticationException.
    Task<MachineIdentity> SignInAsync(CancellationToken cancellationToken = default);
}
