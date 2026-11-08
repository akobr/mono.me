using _42.Testing.UI.Automation.Runtime.Users;

namespace _42.Platform.Cli.AutomationTests.SelfTests;

// The suite's own rules about its settings and its pool, without a target: the KeyValues section of the BYOK cases
// (required with the feature Byok, its shape checked when set), Byok standing for ByokApplication too, and the feature
// users (a test of a user feature leases the user the feature stands for, every other lease excludes those users, and the
// target check signs in another user when the library leased it one of them).
[Category(CliCategories.SelfTest)]
public sealed class SettingsSelfTests
{
    private const string KeyUri = "https://customer.vault.azure.net/keys/cli-tests-kek";
    private const string TenantId = "0b5e4b8e-3d6f-4f3c-9b1e-6a7d2c4f8e10";

    private static readonly TestUser Reader = new(0, "reader@example.test", roles: [PlatformRoles.Reader]);
    private static readonly TestUser Unregistered = new(1, "new@example.test", roles: [PlatformRoles.Reader, CliFeatures.Registration]);
    private static readonly TestUser TwoOrganizations = new(2, "multi@example.test", roles: [PlatformRoles.Owner, CliFeatures.MultiOrganization]);

    [Test]
    public async Task KeyValues_AreRequiredWithByok()
    {
        var withByok = new CliTarget { Features = [CliFeatures.Byok] };

        await Assert.That(Errors(new KeyValueSettings(), withByok)).IsEquivalentTo(new[] { KeyValueSettings.MissingCustomerKey });
        await Assert.That(Errors(new KeyValueSettings { ByokKeyUri = KeyUri }, withByok)).IsEquivalentTo(new[] { KeyValueSettings.MissingCustomerKey });
        await Assert.That(Errors(new KeyValueSettings { ByokKeyUri = KeyUri, ByokTenantId = TenantId }, withByok)).IsEmpty();
        await Assert.That(Errors(new KeyValueSettings(), new CliTarget())).IsEmpty();
    }

    // Byok, a customer key, needs the platform's BYOK application, so it stands for ByokApplication too; not the reverse.
    [Test]
    public async Task Features_ByokImpliesTheByokApplication()
    {
        var withByok = new CliTarget { Features = [CliFeatures.Byok] };
        var withApplication = new CliTarget { Features = [" byokapplication "] };

        await Assert.That(withByok.HasFeature(CliFeatures.ByokApplication)).IsTrue();
        await Assert.That(withApplication.HasFeature(CliFeatures.ByokApplication)).IsTrue();
        await Assert.That(withApplication.HasFeature(CliFeatures.Byok)).IsFalse();
        await Assert.That(new CliTarget().HasFeature(CliFeatures.ByokApplication)).IsFalse();
    }

    [Test]
    public async Task KeyValues_ThatAreSet_HaveTheirShape()
    {
        var target = new CliTarget();

        await Assert.That(Errors(new KeyValueSettings { ByokKeyUri = KeyUri + "/", ByokTenantId = TenantId.ToUpperInvariant() }, target)).IsEmpty();
        await Assert.That(Errors(new KeyValueSettings { ByokKeyUri = KeyUri + "/0123456789abcdef" }, target).Single()).Contains("KeyValues:ByokKeyUri");
        await Assert.That(Errors(new KeyValueSettings { ByokKeyUri = "http://customer.vault.azure.net/keys/kek" }, target).Single()).Contains("KeyValues:ByokKeyUri");
        await Assert.That(Errors(new KeyValueSettings { ByokKeyUri = "https://customer.vault.azure.net/secrets/kek" }, target).Single()).Contains("KeyValues:ByokKeyUri");
        await Assert.That(Errors(new KeyValueSettings { ByokTenantId = "contoso.onmicrosoft.com" }, target).Single()).Contains("KeyValues:ByokTenantId");
    }

    [Test]
    public async Task FeatureUsers_AreLeasedOnlyForTheirFeature()
    {
        var ordinary = FeatureRules.Without(PlatformRoles.RequirementOf(Role.Reader));
        var anyone = FeatureRules.Without(UserRequirement.Any);
        var unregistered = FeatureRules.Without(FeatureRules.Of(CliFeatures.Registration));

        await Assert.That(ordinary.IsSatisfiedBy(Reader)).IsTrue();
        await Assert.That(ordinary.IsSatisfiedBy(Unregistered)).IsFalse();
        await Assert.That(anyone.IsSatisfiedBy(TwoOrganizations)).IsFalse();
        await Assert.That(anyone.IsSatisfiedBy(Unregistered)).IsFalse();
        await Assert.That(unregistered.IsSatisfiedBy(Unregistered)).IsTrue();
        await Assert.That(unregistered.IsSatisfiedBy(Reader)).IsFalse();
        await Assert.That(unregistered.ExcludedRoles).IsEquivalentTo(new[] { CliFeatures.MultiOrganization });
        await Assert.That(CliFeatures.OfUsers.All(feature => CliFeatures.All.Contains(feature))).IsTrue();
    }

    [Test]
    public async Task TargetCheck_ReplacesAFeatureUser()
    {
        var replacement = CliTargetCheck.ReplacementOf(Unregistered);

        await Assert.That(CliTargetCheck.ReplacementOf(Reader)).IsNull();
        await Assert.That(replacement).IsNotNull();
        await Assert.That(replacement!.IsSatisfiedBy(Reader)).IsTrue();
        await Assert.That(replacement.IsSatisfiedBy(Unregistered)).IsFalse();
        await Assert.That(replacement.IsSatisfiedBy(TwoOrganizations)).IsFalse();
        await Assert.That(CliTargetCheck.ReplacementOf(TwoOrganizations)?.IsSatisfiedBy(Reader)).IsTrue();
    }

    private static List<string> Errors(KeyValueSettings settings, CliTarget target)
    {
        var errors = new List<string>();
        settings.Validate(target, errors);
        return errors;
    }

    // CliTest's protected rules of the feature users, reached through a class that derives from it (and has no test).
    private abstract class FeatureRules : CliTest
    {
        public static UserRequirement Without(UserRequirement requirement)
        {
            return WithoutFeatureUsers(requirement);
        }

        public static UserRequirement Of(string feature)
        {
            return FeatureUser(feature);
        }
    }
}
