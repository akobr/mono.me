using System;
using System.Collections;
using System.IO;
using _42.Platform.Cli.Configuration;
using Shouldly;
using Xunit;

namespace _42.Platform.Cli.UnitTests.Configuration;

// The environment decides where the requests and the token go only with a notice on standard error.
public class ConfigurationOverrideNoticeTests
{
    [Fact]
    public void TheApiAddressAndTheSignInSettings_AreNamed_OtherVariablesAreNot()
    {
        var variables = new Hashtable
        {
            ["SFORM_general__baseUrl"] = "https://elsewhere.example/api",
            ["sform_AUTHENTICATION__clientId"] = "client_1",
            ["SFORM_general__userAgent"] = "x",
            ["SFORM_HOME"] = "/tmp/home",
            ["PATH"] = "/bin",
        };
        var error = new StringWriter();

        ConfigurationOverrideNotice.Write(variables, error);

        Lines(error).ShouldBe(
        [
            "! The API address comes from the environment, SFORM_general__baseUrl=https://elsewhere.example/api; sform sends your sign-in there.",
            "! The sign-in settings come from the environment: sform_AUTHENTICATION__clientId.",
        ]);
    }

    [Fact]
    public void WithoutSuchVariables_NothingIsWritten()
    {
        var error = new StringWriter();

        ConfigurationOverrideNotice.Write(new Hashtable { ["SFORM_HOME"] = "/tmp/home", ["SFORM_TOKEN_STORE"] = "file" }, error);

        error.ToString().ShouldBeEmpty();
    }

    private static string[] Lines(StringWriter writer)
    {
        return writer.ToString().ReplaceLineEndings("\n").Split('\n', StringSplitOptions.RemoveEmptyEntries);
    }
}
