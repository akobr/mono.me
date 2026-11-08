using System.IO.Abstractions;
using System.Net;
using System.Net.Sockets;
using _42.Platform.Cli.AutomationTests.Access;
using _42.Platform.Cli.AutomationTests.Running;

namespace _42.Platform.Cli.AutomationTests.SelfTests;

// A place for a self test to run the built sform without a target or a session: a temporary home and working directory
// (deleted at the end), and a runner whose API is a closed port of the loopback, so nothing reaches the network.
internal sealed class SelfTestSandbox : IDisposable
{
    private readonly IFileSystem fileSystem = new FileSystem();
    private readonly string root;

    public SelfTestSandbox(TimeSpan? timeout = null, SformLocator? locator = null)
    {
        root = fileSystem.Path.Combine(fileSystem.Path.GetTempPath(), "sform-selftest-" + Guid.NewGuid().ToString("N")[..12]);
        Home = new SformHome(fileSystem.Path.Combine(root, "home"), SformHomeKind.Fresh, fileSystem).Ensure();
        WorkingDirectory = fileSystem.Path.Combine(root, "work");
        fileSystem.Directory.CreateDirectory(WorkingDirectory);
        ClosedApi = $"http://127.0.0.1:{ClosedPort()}/api";
        Runner = new SformRunner(new SformRunnerOptions
        {
            Locator = locator,
            Timeout = timeout ?? TimeSpan.FromSeconds(90),
            BaseUrl = ClosedApi,
            Home = Home,
            WorkingDirectory = WorkingDirectory,
            CancellationToken = TestContext.Current?.Execution.CancellationToken ?? CancellationToken.None,
            FileSystem = fileSystem,
        });
    }

    public SformHome Home { get; }

    public string WorkingDirectory { get; }

    // An API address nothing listens on.
    public string ClosedApi { get; }

    public SformRunner Runner { get; }

    public IFileSystem FileSystem => fileSystem;

    public Task<SformResult> RunAsync(params string[] arguments)
    {
        return Runner.RunAsync(arguments);
    }

    public void Dispose()
    {
        try
        {
            if (fileSystem.Directory.Exists(root))
            {
                fileSystem.Directory.Delete(root, recursive: true);
            }
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            // a file a killed process still held; the temporary folder is cleaned by the system
        }
    }

    // A port nothing listens on: bound, then released.
    private static int ClosedPort()
    {
        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var port = ((IPEndPoint)listener.LocalEndpoint).Port;
        listener.Stop();
        return port;
    }
}
