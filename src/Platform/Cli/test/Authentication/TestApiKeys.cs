using System;
using System.Text;

namespace _42.Platform.Cli.UnitTests.Authentication;

internal static class TestApiKeys
{
    // A structured machine API key: 2s.{base64url("organization:project:machineId")}.{secret}.
    public static string Create(string organization, string project, string machineId, string secret = "s3cret-value")
    {
        var metadata = Convert.ToBase64String(Encoding.UTF8.GetBytes($"{organization}:{project}:{machineId}"))
            .TrimEnd('=')
            .Replace('+', '-')
            .Replace('/', '_');
        return $"2s.{metadata}.{secret}";
    }
}
