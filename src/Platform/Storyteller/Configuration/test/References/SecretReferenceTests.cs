namespace _42.Platform.Storyteller.Sdk.Configuring.UnitTests.References;

// The vectors of the server's reference check (Backend.CosmosDb/test/ClientEncryptionAndReferenceTests.cs), plus the
// parts of a URI the provider is stricter about.
public class SecretReferenceTests
{
    private const string Version = "0123456789abcdef0123456789abcdef";

    public static TheoryData<string, SecretReferenceForm, string?, string?, string, string?> Accepted()
    {
        return new TheoryData<string, SecretReferenceForm, string?, string?, string, string?>
        {
            // The server's accepted examples.
            { "kv://my-vault/db-password", SecretReferenceForm.Shorthand, "my-vault", null, "db-password", null },
            { $"kv://my-vault/db-password/{Version}", SecretReferenceForm.Shorthand, "my-vault", null, "db-password", Version },
            { "https://other.vault.azure.net/secrets/name", SecretReferenceForm.SecretUri, null, "https://other.vault.azure.net/", "name", null },
            { "  https://other.vault.azure.net/secrets/name/  ", SecretReferenceForm.SecretUri, null, "https://other.vault.azure.net/", "name", null },
            { """{"uri":"https://vault.vault.azure.net/secrets/name"}""", SecretReferenceForm.AppConfiguration, null, "https://vault.vault.azure.net/", "name", null },

            // The limits of the parts, and the cases the server accepts too.
            { $"kv://Vault-01/N-1/{Version.ToUpperInvariant()}", SecretReferenceForm.Shorthand, "Vault-01", null, "N-1", Version.ToUpperInvariant() },
            { $"kv://{new string('v', 24)}/{new string('n', 127)}", SecretReferenceForm.Shorthand, new string('v', 24), null, new string('n', 127), null },
            { $"HTTPS://Other.Vault.Azure.Net/secrets/Name/{Version}/", SecretReferenceForm.SecretUri, null, "https://other.vault.azure.net/", "Name", Version },
            { "https://localhost/secrets/name", SecretReferenceForm.SecretUri, null, "https://localhost/", "name", null },
            { "https://kv.bücher.example/secrets/name", SecretReferenceForm.SecretUri, null, "https://kv.bücher.example/", "name", null },
            { $$"""  { "uri" : " https://vault.vault.azure.net/secrets/name/{{Version}} ", "note": 1 }  """, SecretReferenceForm.AppConfiguration, null, "https://vault.vault.azure.net/", "name", Version },
        };
    }

    public static TheoryData<string?> Rejected()
    {
        return new TheoryData<string?>
        {
            // The server's rejected examples.
            "kv://vault",
            "kv://vault/name/notaversion",
            "http://vault.vault.azure.net/secrets/name",
            "https://vault.vault.azure.net/keys/name",
            "just text",
            """{"url":"x"}""",

            // User info, ports, queries and fragments.
            "https://user@vault.vault.azure.net/secrets/name",
            "https://user:password@vault.vault.azure.net/secrets/name",
            "https://@vault.vault.azure.net/secrets/name",
            "https://vault.vault.azure.net:443/secrets/name",
            "https://vault.vault.azure.net:8443/secrets/name",
            "https://vault.vault.azure.net/secrets/name?api-version=7.4",
            "https://vault.vault.azure.net/secrets/name?",
            "https://vault.vault.azure.net/secrets/name#version",
            "https://vault.vault.azure.net/secrets/name/#",
            """{"uri":"https://vault.vault.azure.net:443/secrets/name"}""",
            """{"uri":"https://vault.vault.azure.net/secrets/name?x=1"}""",

            // Hosts that are not DNS names, and paths that are not written plainly.
            "https://[::1]/secrets/name",
            "https://127.0.0.1/secrets/name",
            "https://my%2Dvault.vault.azure.net/secrets/name",
            "https://vault.vault.azure.net./secrets/name",
            "https://.vault.azure.net/secrets/name",
            "https://vault..azure.net/secrets/name",
            "https://vault.vault.azure.net/x/../secrets/name",
            "https://vault.vault.azure.net/secrets/na%6De",
            "https://vault.vault.azure.net/Secrets/name",
            "https://vault.vault.azure.net/secrets/",
            "https://vault.vault.azure.net/secrets/name/version/extra",
            $"https://vault.vault.azure.net/secrets/name/{Version}//",
            "https:/vault.vault.azure.net/secrets/name",

            // Unicode hosts without an ASCII form that is a DNS name: fullwidth and compatibility delimiters, and Unicode
            // full stops that map to a trailing dot or an empty label.
            "https://evil.com＠myvault.vault.azure.net/secrets/name",
            "https://docs.example.com／.vault.azure.net/secrets/name",
            "https://evil.com＃.vault.azure.net/secrets/name",
            "https://evil.com：8443.vault.azure.net/secrets/name",
            "https://evil.com？.vault.azure.net/secrets/name",
            "https://evil․com.vault.azure.net/secrets/name",
            "https://evil﹒com.vault.azure.net/secrets/name",
            "https://evil﹫com.vault.azure.net/secrets/name",
            "https://⒈vault.vault.azure.net/secrets/name",
            "https://myvault.vault.azure.net。/secrets/name",
            "https://myvault.vault.azure.net．/secrets/name",
            "https://myvault.vault.azure.net｡/secrets/name",
            "https://myvault。。vault.azure.net/secrets/name",
            """{"uri":"https://myvault.vault.azure.net。/secrets/name"}""",

            // The shorthand: its scheme as written, its limits, no trailing slash.
            "KV://vault/name",
            "kv://vault/name/",
            "kv://ab/name",
            $"kv://{new string('v', 25)}/name",
            $"kv://vault/{new string('n', 128)}",
            "kv://vault/na_me",
            "kv://vault.example/name",
            $"kv://vault/name/{Version[..31]}",
            $"kv://vault/name/{Version}0",
            $"kv://vault/name/{Version[..31]}g",

            // App Configuration: an object with one string uri, a secret URI.
            """{"uri":"kv://vault/name"}""",
            """{"uri":"https://vault.vault.azure.net/secrets/name","uri":"https://other.vault.azure.net/secrets/name"}""",
            """{"uri":1}""",
            """{"Uri":"https://vault.vault.azure.net/secrets/name"}""",
            """[{"uri":"https://vault.vault.azure.net/secrets/name"}]""",
            """{'uri':'https://vault.vault.azure.net/secrets/name'}""",
            """{"uri":"https://vault.vault.azure.net/secrets/name" /* comment */}""",
            """{"uri":"https://vault.vault.azure.net/secrets/name",}""",
            """{"uri":"https://vault.vault.azure.net/secrets/name"} {}""",
            "{not json}",

            // A reference inside a longer string is not one.
            "Password=kv://vault/name",
            "kv://vault/name and more",
            "see https://vault.vault.azure.net/secrets/name",
            null,
            string.Empty,
            "   ",
        };
    }

    [Theory]
    [MemberData(nameof(Accepted))]
    public void AcceptedForms_Parse(string value, SecretReferenceForm form, string? vaultName, string? vaultUri, string name, string? version)
    {
        SecretReference.TryParse(value, out var reference).ShouldBeTrue();

        reference.ShouldNotBeNull();
        reference.Original.ShouldBe(value);
        reference.Form.ShouldBe(form);
        reference.VaultName.ShouldBe(vaultName);
        reference.VaultUri.ShouldBe(vaultUri is null ? null : new Uri(vaultUri));
        reference.Name.ShouldBe(name);
        reference.Version.ShouldBe(version);
        reference.ToString().ShouldBe(value);
    }

    [Theory]
    [MemberData(nameof(Rejected))]
    public void OtherValues_DoNotParse(string? value)
    {
        SecretReference.TryParse(value, out var reference).ShouldBeFalse();

        reference.ShouldBeNull();
    }

    [Fact]
    public void TheVaultUriOfAnIdnHost_HasItsAsciiForm()
    {
        SecretReference.TryParse("https://kv.bücher.example/secrets/name", out var reference).ShouldBeTrue();

        reference.VaultUri.ShouldNotBeNull().IdnHost.ShouldBe("kv.xn--bcher-kva.example");
    }
}
