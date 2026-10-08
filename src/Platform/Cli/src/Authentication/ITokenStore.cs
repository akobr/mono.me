using System;
using System.Threading;
using System.Threading.Tasks;

namespace _42.Platform.Cli.Authentication;

// Persistent AuthKit session. IFileSystem does not cover the OS-protected storage, hence the abstraction.
public interface ITokenStore
{
    Task<AuthKitSession?> ReadAsync(CancellationToken cancellationToken = default);

    Task WriteAsync(AuthKitSession session, CancellationToken cancellationToken = default);

    Task ClearAsync(CancellationToken cancellationToken = default);

    // Exclusive across sform processes, so two of them never rotate the same refresh token.
    Task<IDisposable> LockAsync(CancellationToken cancellationToken = default);
}
