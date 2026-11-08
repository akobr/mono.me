using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Shouldly;
using Xunit;

namespace _42.Platform.Cli.UnitTests;

// The built sform started the way a program starts it: standard input, output and error all redirected, so on Windows
// the process has no console at all. Every themed line used to crash it there at Console.BufferWidth (exit 1, "Total
// mayhem"), before anything a test could read. The build of the CLI sits next to the tests (a project reference).
public sealed class ProgramProcessTests : IDisposable
{
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(90);

    private readonly string _home = Path.Combine(Path.GetTempPath(), "sform-process-" + Guid.NewGuid().ToString("N"));

    [Fact]
    public async Task AnInputError_IsWritten_AndEndsWithItsCode()
    {
        var result = await RunAsync(["account", "set", "-p", "bad"]);

        result.ExitCode.ShouldBe(ExitCodes.ERROR_WRONG_INPUT, result.ToString());
        result.Output.ShouldContain("! 'bad' is no project key; it takes <organization>.<project>.");
        result.Output.ShouldNotContain("Total mayhem");
    }

    // SGN-MSI-01 without an API: an API key signs in without a call, and the summary has a header.
    [Fact]
    public async Task AMachineSignIn_WritesItsHeaderAndSummary()
    {
        var result = await RunAsync(["account"], MachineVariables($"http://127.0.0.1:{ClosedPort()}/api"));

        result.ExitCode.ShouldBe(ExitCodes.SUCCESS, result.ToString());
        result.Lines.ShouldContain("# Machine sign-in");
        result.Lines.ShouldContain("  Signed in as machine m1 with an API key (STORYTELLER_API_KEY).");
        result.Lines.ShouldContain("  Project org1.proj1 with default view.");
        result.Output.ShouldNotContain("not-a-real-secret");
    }

    // GEN-CLI-04, and SFORM_general__baseUrl: the API of the run is the closed port, which is a service failure.
    [Fact]
    public async Task AnUnreachableApi_IsAServiceFailure()
    {
        var baseUrl = $"http://127.0.0.1:{ClosedPort()}/api";

        var result = await RunAsync(["story", "-p", "org1.proj1"], new Dictionary<string, string> { ["SFORM_general__baseUrl"] = baseUrl });

        result.ExitCode.ShouldBe(ExitCodes.ERROR_SERVICE_FAILURE, result.ToString());
        result.Output.ShouldContain("! The 2S platform could not be reached: ");
        result.Output.ShouldContain($"The API is {baseUrl} ");
        result.Output.ShouldNotContain("Total mayhem");
    }

    // GEN-CLI-01: the help of the key/value tree. KeyValueListCommand, the parent of every kv command, once took an
    // optional TimeProvider that the container did not have, so every kv command line (--help too) exited with 1.
    [Theory]
    [InlineData("story", "kv", "--help")]
    [InlineData("story", "key-values", "get", "--help")]
    [InlineData("story", "kv", "policy", "set", "--help")]
    public async Task TheHelpOfTheKeyValueCommands_IsWritten(params string[] args)
    {
        var result = await RunAsync(args);

        result.ExitCode.ShouldBe(ExitCodes.SUCCESS, result.ToString());
        result.Output.ShouldContain("Usage: sform storyteller kv");
        result.Output.ShouldNotContain("Total mayhem");
    }

    // GEN-CLI-05 without an API: the kv commands reach the API, whose closed port is a service failure, not a crash.
    [Theory]
    [InlineData("story", "kv", "get", "x", "-p", "org1.proj1")]
    [InlineData("story", "kv", "-p", "org1.proj1")]
    public async Task AKeyValueCommand_OfAMachine_ReachesTheApi(params string[] args)
    {
        var baseUrl = $"http://127.0.0.1:{ClosedPort()}/api";

        var result = await RunAsync(args, MachineVariables(baseUrl));

        result.ExitCode.ShouldBe(ExitCodes.ERROR_SERVICE_FAILURE, result.ToString());
        result.Output.ShouldContain("! The 2S platform could not be reached: ");
        result.Output.ShouldContain($"The API is {baseUrl} ");
        result.Output.ShouldNotContain("Total mayhem");
    }

    public void Dispose()
    {
        if (Directory.Exists(_home))
        {
            Directory.Delete(_home, recursive: true);
        }
    }

    // The API key of machine m1 of org1.proj1, which signs in without a call.
    private static Dictionary<string, string> MachineVariables(string endpoint)
    {
        var machine = Convert.ToBase64String(Encoding.UTF8.GetBytes("org1:proj1:m1")).TrimEnd('=').Replace('+', '-').Replace('/', '_');

        return new Dictionary<string, string>
        {
            ["STORYTELLER_ENDPOINT"] = endpoint,
            ["STORYTELLER_API_KEY"] = $"2s.{machine}.not-a-real-secret",
        };
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

    // The dotnet host that runs the tests, else the one on the PATH.
    private static string DotnetHost()
    {
        var current = Environment.ProcessPath;

        if (current is not null && Path.GetFileNameWithoutExtension(current).Equals("dotnet", StringComparison.OrdinalIgnoreCase))
        {
            return current;
        }

        return Environment.GetEnvironmentVariable("DOTNET_HOST_PATH") is { Length: > 0 } hostPath ? hostPath : "dotnet";
    }

    private async Task<ProcessResult> RunAsync(IReadOnlyList<string> args, IReadOnlyDictionary<string, string>? variables = null)
    {
        var startInfo = new ProcessStartInfo(DotnetHost())
        {
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
            WorkingDirectory = Path.GetTempPath(),
        };

        startInfo.ArgumentList.Add(Path.Combine(AppContext.BaseDirectory, "42.Platform.Cli.dll"));

        foreach (var arg in args)
        {
            startInfo.ArgumentList.Add(arg);
        }

        // Nothing of the machine the tests run on: no sign-in, no state, no telemetry, no user secrets.
        foreach (var name in startInfo.Environment.Keys.Where(IsInherited).ToList())
        {
            startInfo.Environment.Remove(name);
        }

        startInfo.Environment["SFORM_HOME"] = _home;
        startInfo.Environment["DO_NOT_TRACK"] = "1";
        startInfo.Environment["DOTNET_ENVIRONMENT"] = "Production";

        foreach (var (name, value) in variables ?? new Dictionary<string, string>())
        {
            startInfo.Environment[name] = value;
        }

        using var process = Process.Start(startInfo) ?? throw new InvalidOperationException("sform did not start.");
        process.StandardInput.Close();
        var output = process.StandardOutput.ReadToEndAsync(TestContext.Current.CancellationToken);
        var error = process.StandardError.ReadToEndAsync(TestContext.Current.CancellationToken);
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        timeout.CancelAfter(Timeout);

        try
        {
            await process.WaitForExitAsync(timeout.Token);
        }
        catch (OperationCanceledException)
        {
            process.Kill(entireProcessTree: true);
            throw;
        }

        return new ProcessResult(process.ExitCode, await output, await error);
    }

    private static bool IsInherited(string name)
    {
        return name.StartsWith("STORYTELLER_", StringComparison.OrdinalIgnoreCase)
            || name.StartsWith("SFORM_", StringComparison.OrdinalIgnoreCase)
            || name.Equals("BROWSER", StringComparison.OrdinalIgnoreCase)
            || name.Equals("DOTNET_ENVIRONMENT", StringComparison.OrdinalIgnoreCase);
    }

    private sealed record ProcessResult(int ExitCode, string Output, string Error)
    {
        public IReadOnlyList<string> Lines => Output.Split('\n').Select(line => line.TrimEnd('\r')).ToList();

        public override string ToString()
        {
            return $"exit {ExitCode}{Environment.NewLine}stdout:{Environment.NewLine}{Output}{Environment.NewLine}stderr:{Environment.NewLine}{Error}";
        }
    }
}
