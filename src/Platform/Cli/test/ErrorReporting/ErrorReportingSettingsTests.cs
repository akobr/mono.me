using System.Collections;
using System.Linq;
using _42.Platform.Cli.Authentication;
using _42.Platform.Cli.Configuration;
using _42.Platform.Cli.ErrorReporting;
using Moq;
using Shouldly;
using Xunit;

namespace _42.Platform.Cli.UnitTests.ErrorReporting;

// What Sentry never shows though the command line did not give it, and the secrets the local log never shows either.
public class ErrorReportingSettingsTests
{
    [Fact]
    public void TheContext_HoldsTheDefaultsTheMachineTheVariablesAndThePerson()
    {
        var variables = new Hashtable
        {
            ["STORYTELLER_CLIENT_SECRET"] = "cl1ent-s3cret",
            ["STORYTELLER_CLIENT_CERTIFICATE_PASSWORD"] = "p4ss",
            ["STORYTELLER_KEY"] = "@acme.billing.prod.sbt.app",
            ["SFORM_HOME"] = @"C:\Users\ada\.sform",
            ["PATH"] = @"C:\Windows",
        };
        var machine = MachineSignInSettings.FromEnvironment(name => variables[name] as string);

        var settings = Create(variables, machine, new AccessDefaultOptions { OrganizationName = "northwind", ProjectName = "invoicing", ViewName = "prod" });

        settings.ContextValues.ShouldContain("northwind");
        settings.ContextValues.ShouldContain("invoicing");
        settings.ContextValues.ShouldContain("acme");
        settings.ContextValues.ShouldContain("billing");
        settings.ContextValues.ShouldContain(@"C:\Users\ada\.sform");
        settings.ContextValues.ShouldContain("cl1ent-s3cret");
        settings.ContextValues.ShouldContain("ada-laptop");
        settings.ContextValues.ShouldNotContain(@"C:\Windows");
        settings.SecretValues.ShouldBe(["cl1ent-s3cret", "p4ss"], ignoreOrder: true);
        settings.Dsn.ShouldBe("https://public@o1.ingest.de.sentry.io/1");
        settings.Release.ShouldBe("42.Platform.Cli@1.2.3");
    }

    [Fact]
    public void TheIdentityOfTheMachine_IsReadWhenAReportIsMade()
    {
        var signIn = new Mock<IMachineSignIn>();
        var settings = Create(new Hashtable(), null, null, signIn.Object);

        signIn.Setup(machine => machine.TryGetIdentity())
            .Returns(new MachineIdentity(MachineSignInKind.ApiKey, ["STORYTELLER_API_KEY"], "m1", "globex", "payroll"));

        settings.ReportTimeValues.ShouldNotBeNull()().ShouldBe(["globex", "payroll", "m1"]);
    }

    [Fact]
    public void NothingKnown_GivesNoValues()
    {
        var settings = Create(null, null, null);

        settings.ContextValues.OfType<string>().ShouldBe(["ada", "ada-laptop"], ignoreOrder: true);
        settings.SecretValues.ShouldBeEmpty();
        settings.ReportTimeValues.ShouldBeNull();
    }

    private static ErrorReportingSettings Create(IDictionary? variables, MachineSignInSettings? machine, AccessDefaultOptions? access, IMachineSignIn? signIn = null)
    {
        return ErrorReportingSettings.Create(
            new LoggingOptions { SentryDsn = "https://public@o1.ingest.de.sentry.io/1" },
            isOptedOut: false,
            installationId: null,
            environmentName: "production",
            release: "42.Platform.Cli@1.2.3",
            access,
            machine,
            signIn,
            variables,
            ["ada", "ada-laptop"]);
    }
}
