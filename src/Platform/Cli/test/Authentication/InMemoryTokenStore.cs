using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using _42.Platform.Cli.Authentication;

namespace _42.Platform.Cli.UnitTests.Authentication;

internal sealed class InMemoryTokenStore : ITokenStore
{
    private readonly Queue<AuthKitSession?> _scriptedReads = new();

    public AuthKitSession? Session { get; set; }

    public int Locks { get; private set; }

    public int Writes { get; private set; }

    public int Clears { get; private set; }

    // Returned by the next reads instead of Session, to simulate another sform process.
    public void ScriptReads(params AuthKitSession?[] sessions)
    {
        foreach (var session in sessions)
        {
            _scriptedReads.Enqueue(session);
        }
    }

    public Task<AuthKitSession?> ReadAsync(CancellationToken cancellationToken = default)
    {
        return Task.FromResult(_scriptedReads.Count > 0 ? _scriptedReads.Dequeue() : Session);
    }

    public Task WriteAsync(AuthKitSession session, CancellationToken cancellationToken = default)
    {
        Writes++;
        Session = session;
        return Task.CompletedTask;
    }

    public Task ClearAsync(CancellationToken cancellationToken = default)
    {
        Clears++;
        Session = null;
        return Task.CompletedTask;
    }

    public Task<IDisposable> LockAsync(CancellationToken cancellationToken = default)
    {
        Locks++;
        return Task.FromResult<IDisposable>(new NoopDisposable());
    }

    private sealed class NoopDisposable : IDisposable
    {
        public void Dispose()
        {
        }
    }
}
