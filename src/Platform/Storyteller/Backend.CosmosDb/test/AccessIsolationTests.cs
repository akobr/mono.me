#nullable enable

using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Text.Json;
using System.Threading.Tasks;
using _42.Platform.Storyteller.Accessing;
using _42.Platform.Storyteller.Accessing.Federation;
using _42.Platform.Storyteller.Accessing.Model;
using _42.Platform.Storyteller.Entities.Access;
using FluentAssertions;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;
using Xunit;

namespace _42.Platform.Storyteller.Backend.CosmosDb.UnitTests;

// Tenant isolation of machines, machine certificates, shared certificates and trusted issuers in the shared container
// organizations. Two organizations hold the same project names, machine ids, thumbprints, labels and issuer names,
// written through the real services. Whatever is read through A is A's (its values carry "a-", B's "b-"), and whatever
// is written through A leaves B's items as they were (the same ETag).
public class AccessIsolationTests(Startup startup)
    : BaseTestsClass(startup)
{
    // One pair of organizations per run of the class, so a reused emulator never mixes runs; every test has a project
    // of its own, with the same name in both organizations.
    private static readonly string Suffix = Guid.NewGuid().ToString("N")[..8];
    private static readonly string OrgA = $"iso-access-a-{Suffix}";
    private static readonly string OrgB = $"iso-access-b-{Suffix}";

    private static readonly MachineScope[] Granted = [MachineScope.AnnotationRead, MachineScope.ConfigurationRead];

    private IContainerRepositoryProvider Repositories => Context.Services.GetRequiredService<IContainerRepositoryProvider>();

    private IOptions<JsonSerializerOptions> SerializerOptions => Context.Services.GetRequiredService<IOptions<JsonSerializerOptions>>();

    private IApiKeyHashStore HashStore => Context.Services.GetRequiredService<IApiKeyHashStore>();

    [Fact]
    public async Task Machines_WithTheSameIdAndProject_AreListedReadResetAndDeletedOnlyInTheirOrganization()
    {
        const string project = "billing";
        var partition = AccessPartition(project);
        var shared = NewId();
        var onlyA = NewId();
        var onlyB = NewId();
        await CreateMachineAsync(OrgA, project, shared, "a");
        await CreateMachineAsync(OrgA, project, onlyA, "a");
        await CreateMachineAsync(OrgB, project, shared, "b");
        await CreateMachineAsync(OrgB, project, onlyB, "b");
        var access = AccessService(new TaggedMachineAccessService(HashStore, "a"));

        // the same id twice: two items, each with its own organization, the first level of its key
        (await StoredOrganizationAsync(OrgA, partition, shared)).Should().Be(OrgA);
        (await StoredOrganizationAsync(OrgB, partition, shared)).Should().Be(OrgB);

        var listA = (await access.GetMachineAccessesAsync(OrgA, project)).ToList();
        listA.Select(machine => machine.Id).Should().BeEquivalentTo(new[] { shared, onlyA });
        listA.Should().OnlyContain(machine => machine.AnnotationKey == "a-unit" && machine.AccessKey == "a-k***");
        var listB = (await access.GetMachineAccessesAsync(OrgB, project)).ToList();
        listB.Select(machine => machine.Id).Should().BeEquivalentTo(new[] { shared, onlyB });
        listB.Should().OnlyContain(machine => machine.AnnotationKey == "b-unit" && machine.AccessKey == "b-k***");

        (await access.GetMachineAccessAsync(OrgA, project, shared))!.AnnotationKey.Should().Be("a-unit");
        (await access.GetMachineAccessAsync(OrgB, project, shared))!.AnnotationKey.Should().Be("b-unit");
        (await access.GetMachineAccessAsync(OrgA, project, onlyB)).Should().BeNull();

        (await access.VerifyAccessForMachineAsync(OrgA, project, shared)).Should().BeTrue();
        (await access.VerifyAccessForMachineAsync(OrgA, project, onlyB)).Should().BeFalse("B's machine is no machine of A");
        (await access.VerifyAccessForMachineAsync(OrgB, project, onlyA)).Should().BeFalse("A's machine is no machine of B");
        (await access.VerifyAccessForMachineAsync($"iso-access-none-{Suffix}", project, shared)).Should().BeFalse("an organization without data has no machine");

        (await HashStore.GetAsync(OrgA, project, shared))!.HashedSecret.Should().Be("a-hash-1");
        (await HashStore.GetAsync(OrgB, project, shared))!.HashedSecret.Should().Be("b-hash-1");
        (await HashStore.GetAsync(OrgA, project, onlyB)).Should().BeNull();

        var sharedOfB = await ETagAsync(OrgB, partition, shared);
        var onlyOfB = await ETagAsync(OrgB, partition, onlyB);

        // a reset through A replaces A's hash only, and doesn't find B's machine
        var reset = await access.ResetMachineAccessAsync(OrgA, project, shared);
        reset.AccessKey.Should().Be("a-key-2");
        (await HashStore.GetAsync(OrgA, project, shared))!.HashedSecret.Should().Be("a-hash-2");
        (await HashStore.GetAsync(OrgB, project, shared))!.HashedSecret.Should().Be("b-hash-1");
        var resetOfB = () => access.ResetMachineAccessAsync(OrgA, project, onlyB);
        await resetOfB.Should().ThrowAsync<NotFoundException>();

        // a delete through A removes A's machine only
        (await access.DeleteMachineAccessAsync(OrgA, project, shared)).Should().BeTrue();
        (await access.DeleteMachineAccessAsync(OrgA, project, onlyB)).Should().BeFalse("B's machine is no machine of A");
        (await access.GetMachineAccessAsync(OrgA, project, shared)).Should().BeNull();
        (await access.VerifyAccessForMachineAsync(OrgB, project, shared)).Should().BeTrue();
        (await access.GetMachineAccessesAsync(OrgB, project)).Select(machine => machine.Id).Should().BeEquivalentTo(new[] { shared, onlyB });
        (await HashStore.GetAsync(OrgB, project, shared))!.HashedSecret.Should().Be("b-hash-1");
        (await ETagAsync(OrgB, partition, shared)).Should().Be(sharedOfB, "nothing done through A writes B's machine");
        (await ETagAsync(OrgB, partition, onlyB)).Should().Be(onlyOfB, "nothing done through A writes B's machine");
    }

    [Fact]
    public async Task FederatedMachines_OfTheSameIssuerName_AreFoundAndChangedOnlyInTheirOrganization()
    {
        const string project = "deploy";
        var partition = AccessPartition(project);
        var shared = NewId();
        var onlyB = NewId();
        await CreateMachineAsync(OrgA, project, shared, "a", MachineCredentialKind.Federated, rules: [Rule("github", "repo:a/app:*")]);
        await CreateMachineAsync(OrgB, project, shared, "b", MachineCredentialKind.Federated, rules: [Rule("github", "repo:b/app:*")]);
        await CreateMachineAsync(OrgB, project, onlyB, "b", MachineCredentialKind.Federated, rules: [Rule("github", "repo:b/tool:*")]);
        var access = AccessService();

        (await access.FindMachinesUsingIssuerAsync(OrgA, project, "github", 10)).Should().Equal(shared);
        (await access.FindMachinesUsingIssuerAsync(OrgB, project, "github", 10)).Should().BeEquivalentTo(new[] { shared, onlyB });

        var sharedOfB = await ETagAsync(OrgB, partition, shared);
        var onlyOfB = await ETagAsync(OrgB, partition, onlyB);

        var updated = await access.SetMachineFederationRulesAsync(OrgA, project, shared, [Rule("github", "repo:a/other:*")]);
        updated.FederationRules!.Single().Subject.Should().Be("repo:a/other:*");
        (await access.GetMachineAccessAsync(OrgA, project, shared))!.FederationRules!.Single().Subject.Should().Be("repo:a/other:*");
        (await access.GetMachineAccessAsync(OrgB, project, shared))!.FederationRules!.Single().Subject.Should().Be("repo:b/app:*");
        var rulesOfB = () => access.SetMachineFederationRulesAsync(OrgA, project, onlyB, [Rule("github", "repo:a/app:*")]);
        await rulesOfB.Should().ThrowAsync<NotFoundException>();

        (await ETagAsync(OrgB, partition, shared)).Should().Be(sharedOfB, "nothing done through A writes B's machine");
        (await ETagAsync(OrgB, partition, onlyB)).Should().Be(onlyOfB, "nothing done through A writes B's machine");
        (await access.FindMachinesUsingIssuerAsync(OrgA, project, "github", 10)).Should().Equal(shared);
    }

    [Fact]
    public async Task TrustedIssuers_WithTheSameName_AreListedReadReplacedAndDeletedOnlyInTheirOrganization()
    {
        const string project = "trust";
        var partition = CosmosFederatedIssuerStore.GetPartition(project);
        await Issuers().SetAsync(OrgA, project, NewIssuer("github", "a"));
        await Issuers().SetAsync(OrgB, project, NewIssuer("github", "b"));
        await Issuers().SetAsync(OrgB, project, NewIssuer("gitlab", "b"));

        var listA = await Issuers().ListAsync(OrgA, project);
        listA.Select(issuer => issuer.Name).Should().Equal("github");
        listA.Single().Issuer.Should().Be("https://a.github.example.com");
        var listB = await Issuers().ListAsync(OrgB, project);
        listB.Select(issuer => issuer.Name).Should().Equal("github", "gitlab");
        listB.Should().OnlyContain(issuer => issuer.Issuer.StartsWith("https://b.", StringComparison.Ordinal) && issuer.Description == "b-" + issuer.Name);

        (await Issuers().GetAsync(OrgA, project, "github"))!.Description.Should().Be("a-github");
        (await Issuers().GetAsync(OrgB, project, "github"))!.Description.Should().Be("b-github");
        (await Issuers().GetAsync(OrgA, project, "gitlab")).Should().BeNull();

        var githubOfB = await ETagAsync(OrgB, partition, "github");
        var gitlabOfB = await ETagAsync(OrgB, partition, "gitlab");

        // the replace compares the Issuer with the stored one: B's differs, so a read of B's issuer would refuse it
        await Issuers().SetAsync(OrgA, project, NewIssuer("github", "a") with { Description = "a-github-2" });
        (await Issuers().GetAsync(OrgA, project, "github"))!.Description.Should().Be("a-github-2");
        (await Issuers().GetAsync(OrgB, project, "github"))!.Description.Should().Be("b-github");

        (await Issuers().DeleteAsync(OrgA, project, "gitlab")).Should().BeFalse("only B trusts gitlab");
        (await Issuers().DeleteAsync(OrgA, project, "github")).Should().BeTrue();
        (await Issuers().GetAsync(OrgA, project, "github")).Should().BeNull();
        (await Issuers().ListAsync(OrgA, project)).Should().BeEmpty();
        (await Issuers().ListAsync(OrgB, project)).Select(issuer => issuer.Name).Should().Equal("github", "gitlab");
        (await ETagAsync(OrgB, partition, "github")).Should().Be(githubOfB, "nothing done through A writes B's issuer");
        (await ETagAsync(OrgB, partition, "gitlab")).Should().Be(gitlabOfB, "nothing done through A writes B's issuer");
    }

    [Fact]
    public async Task TheIssuerLimit_CountsOnlyTheIssuersOfTheOrganization()
    {
        const string project = "limits";

        for (var index = 1; index <= FederationValidation.MaxIssuersPerProject; index++)
        {
            await Issuers().SetAsync(OrgB, project, NewIssuer($"issuer-{index:D2}", "b"));
        }

        // B's project is full; A's project of the same name has no issuer, so a count of B's would refuse A's first one
        await Issuers().SetAsync(OrgA, project, NewIssuer("issuer-01", "a"));
        var overTheLimit = () => Issuers().SetAsync(OrgB, project, NewIssuer("issuer-21", "b"));

        (await overTheLimit.Should().ThrowAsync<InvalidInputException>()).Which.Message.Should().Be(FederationValidation.TooManyIssuersMessage);
        (await Issuers().ListAsync(OrgA, project)).Select(issuer => issuer.Name).Should().Equal("issuer-01");
        (await Issuers().ListAsync(OrgB, project)).Should().HaveCount(FederationValidation.MaxIssuersPerProject);
    }

    [Fact]
    public async Task SharedCertificates_WithTheSameThumbprintAndLabel_StayInTheirOrganization()
    {
        // the same labels in both organizations; A's certificates expire in 2030, B's in 2031
        const string project = "edge";
        var partition = AccessPartition(project);
        var shared = NewThumbprint();
        var onlyB = NewThumbprint();
        await SharedCertificates().StoreAsync(OrgA, project, NewSharedCertificate(shared, "edge", 2030));
        await SharedCertificates().StoreAsync(OrgB, project, NewSharedCertificate(shared, "edge", 2031));
        await SharedCertificates().StoreAsync(OrgB, project, NewSharedCertificate(onlyB, "b-only", 2031));

        var listA = await SharedCertificates().ListAsync(OrgA, project);
        listA.Should().ContainSingle().Which.Thumbprint.Should().Be(shared);
        listA.Single().NotAfter.Year.Should().Be(2030);
        var listB = await SharedCertificates().ListAsync(OrgB, project);
        listB.Select(certificate => certificate.Thumbprint).Should().BeEquivalentTo(new[] { shared, onlyB });
        listB.Should().OnlyContain(certificate => certificate.NotAfter.Year == 2031);

        (await SharedCertificates().GetByThumbprintAsync(OrgA, project, shared))!.NotAfter.Year.Should().Be(2030);
        (await SharedCertificates().GetByThumbprintAsync(OrgB, project, shared))!.NotAfter.Year.Should().Be(2031);
        (await SharedCertificates().GetByThumbprintAsync(OrgA, project, onlyB)).Should().BeNull();
        (await SharedCertificates().LabelExistsAsync(OrgA, project, "edge")).Should().BeTrue();
        (await SharedCertificates().LabelExistsAsync(OrgA, project, "b-only")).Should().BeFalse("only B has the label");
        (await SharedCertificates().LabelExistsAsync(OrgB, project, "b-only")).Should().BeTrue();

        var sharedOfB = await ETagAsync(OrgB, partition, SharedCertificateId(shared));
        var onlyOfB = await ETagAsync(OrgB, partition, SharedCertificateId(onlyB));

        (await SharedCertificates().RevokeAsync(OrgA, project, shared)).Should().BeTrue();
        (await SharedCertificates().RevokeAsync(OrgA, project, onlyB)).Should().BeFalse("B's certificate is no certificate of A");
        (await SharedCertificates().GetByThumbprintAsync(OrgA, project, shared))!.IsRevoked.Should().BeTrue();
        (await SharedCertificates().GetByThumbprintAsync(OrgB, project, shared))!.IsRevoked.Should().BeFalse();

        // a certificate only B has is new to A: the same id in another organization is another item, not a conflict
        await SharedCertificates().StoreAsync(OrgA, project, NewSharedCertificate(onlyB, "a-late", 2030));
        (await SharedCertificates().GetByThumbprintAsync(OrgA, project, onlyB))!.Label.Should().Be("a-late");
        (await SharedCertificates().GetByThumbprintAsync(OrgB, project, onlyB))!.Label.Should().Be("b-only");

        (await ETagAsync(OrgB, partition, SharedCertificateId(shared))).Should().Be(sharedOfB, "nothing done through A writes B's certificate");
        (await ETagAsync(OrgB, partition, SharedCertificateId(onlyB))).Should().Be(onlyOfB, "nothing done through A writes B's certificate");
    }

    [Fact]
    public async Task MachineCertificates_OfTheSameMachineAndThumbprint_StayInTheirOrganization()
    {
        const string project = "workers";
        var partition = AccessPartition(project);
        var machine = NewId();
        var onlyB = NewId();
        var thumbprint = NewThumbprint();
        var thumbprintOfOnlyB = NewThumbprint();

        // The records have no kind, so the store takes it from the machine document: A's machine and B's differ in it.
        await CreateMachineAsync(OrgA, project, machine, "a", MachineCredentialKind.Certificate, thumbprint);
        await CreateMachineAsync(OrgB, project, machine, "b", MachineCredentialKind.CertificateAndApiKey, thumbprint);
        await ClientCertificates().StoreAsync(OrgA, project, NewRecord(machine, thumbprint, "a"));
        await ClientCertificates().StoreAsync(OrgB, project, NewRecord(machine, thumbprint, "b"));
        await ClientCertificates().StoreAsync(OrgB, project, NewRecord(onlyB, thumbprintOfOnlyB, "b"));

        var ofA = (await ClientCertificates().GetByThumbprintAsync(OrgA, project, thumbprint))!;
        ofA.SerialNumber.Should().Be("a-serial");
        ofA.AnnotationKey.Should().Be("a-unit");
        ofA.CredentialKind.Should().Be(MachineCredentialKind.Certificate);
        var ofB = (await ClientCertificates().GetByThumbprintAsync(OrgB, project, thumbprint))!;
        ofB.SerialNumber.Should().Be("b-serial");
        ofB.AnnotationKey.Should().Be("b-unit");
        ofB.CredentialKind.Should().Be(MachineCredentialKind.CertificateAndApiKey);
        (await ClientCertificates().GetByMachineAccessIdAsync(OrgA, project, machine))!.SerialNumber.Should().Be("a-serial");
        (await ClientCertificates().GetByMachineAccessIdAsync(OrgB, project, machine))!.SerialNumber.Should().Be("b-serial");
        (await ClientCertificates().GetByMachineAccessIdAsync(OrgA, project, onlyB)).Should().BeNull();
        (await ClientCertificates().GetByThumbprintAsync(OrgA, project, thumbprintOfOnlyB)).Should().BeNull();

        var recordOfB = await ETagAsync(OrgB, partition, MachineCertificateEntity.GetId(thumbprint));
        var onlyOfB = await ETagAsync(OrgB, partition, MachineCertificateEntity.GetId(thumbprintOfOnlyB));

        await ClientCertificates().RevokeAsync(OrgA, project, machine);
        await ClientCertificates().RevokeAsync(OrgA, project, onlyB);

        (await ClientCertificates().GetByMachineAccessIdAsync(OrgA, project, machine))!.IsRevoked.Should().BeTrue();
        (await ClientCertificates().GetByThumbprintAsync(OrgB, project, thumbprint))!.IsRevoked.Should().BeFalse();
        (await ClientCertificates().GetByMachineAccessIdAsync(OrgB, project, onlyB))!.IsRevoked.Should().BeFalse();
        (await ETagAsync(OrgB, partition, MachineCertificateEntity.GetId(thumbprint))).Should().Be(recordOfB, "nothing done through A writes B's record");
        (await ETagAsync(OrgB, partition, MachineCertificateEntity.GetId(thumbprintOfOnlyB))).Should().Be(onlyOfB, "nothing done through A writes B's record");
    }

    [Fact]
    public async Task CertificateRenewal_OfTheSameMachineAndCertificate_RenewsOnlyInItsOrganization()
    {
        const string project = "renewals";
        var partition = AccessPartition(project);
        var machine = NewId();
        var onlyB = NewId();
        var thumbprint = NewThumbprint();
        var thumbprintOfOnlyB = NewThumbprint();
        var renewed = NewThumbprint();
        await CreateMachineAsync(OrgA, project, machine, "a", MachineCredentialKind.Certificate, thumbprint);
        await CreateMachineAsync(OrgB, project, machine, "b", MachineCredentialKind.Certificate, thumbprint);
        await CreateMachineAsync(OrgB, project, onlyB, "b", MachineCredentialKind.Certificate, thumbprintOfOnlyB);
        var machineOfB = await ETagAsync(OrgB, partition, machine);
        var onlyOfB = await ETagAsync(OrgB, partition, onlyB);

        // strict: a certificate issued for anything but A's machine fails the test
        var authority = new Mock<IClientCertificateAuthority>(MockBehavior.Strict);
        authority
            .Setup(ca => ca.IssueCertificateAsync(OrgA, project, machine, It.IsAny<int>()))
            .ReturnsAsync(new IssuedCertificate([], "a-password", renewed, "a-serial-2", DateTimeOffset.UtcNow, DateTimeOffset.UtcNow.AddDays(90)));
        var renewal = Renewal(authority.Object);

        (await renewal.RenewAsync(OrgA, project, machine, thumbprint)).Outcome.Should().Be(CertificateRenewalOutcome.Success);
        (await renewal.RenewAsync(OrgA, project, onlyB, thumbprintOfOnlyB)).Outcome.Should().Be(CertificateRenewalOutcome.NotFound);

        var access = AccessService();
        (await access.GetMachineAccessAsync(OrgA, project, machine))!.CertificateThumbprint.Should().Be(renewed);
        (await access.GetMachineAccessAsync(OrgB, project, machine))!.CertificateThumbprint.Should().Be(thumbprint);
        (await ETagAsync(OrgB, partition, machine)).Should().Be(machineOfB, "a renewal through A writes A's machine only");
        (await ETagAsync(OrgB, partition, onlyB)).Should().Be(onlyOfB, "a renewal through A writes A's machine only");
        authority.Verify(ca => ca.IssueCertificateAsync(OrgA, project, machine, It.IsAny<int>()), Times.Once);
    }

    private static string NewId() => Guid.NewGuid().ToString("D");

    private static string NewThumbprint() => Guid.NewGuid().ToString("N").ToUpperInvariant();

    // The partition of machines, machine certificates and shared certificates of a project.
    private static string AccessPartition(string project) => $"{project}.access";

    // The id CosmosSharedCertificateStore gives a shared certificate.
    private static string SharedCertificateId(string thumbprint) => $"scr.{thumbprint}";

    private static FederationRule Rule(string issuer, string subject) => new()
    {
        Issuer = issuer,
        Audience = "urn:storyteller:test.platform:api",
        Subject = subject,
    };

    private static FederatedIssuer NewIssuer(string name, string tag) => new()
    {
        Name = name,
        Issuer = $"https://{tag}.{name}.example.com",
        Description = $"{tag}-{name}",
    };

    private static SharedCertificate NewSharedCertificate(string thumbprint, string label, int year) => new()
    {
        Thumbprint = thumbprint,
        Label = label,
        NotBefore = new DateTimeOffset(year - 1, 1, 1, 0, 0, 0, TimeSpan.Zero),
        NotAfter = new DateTimeOffset(year, 1, 1, 0, 0, 0, TimeSpan.Zero),
    };

    // A record without the kind of its machine, as stored before the kind was kept on it.
    private static CertificateRecord NewRecord(string machineId, string thumbprint, string tag) => new(
        machineId,
        thumbprint,
        null,
        null,
        $"{tag}-serial",
        DateTimeOffset.UtcNow.AddMinutes(-1),
        DateTimeOffset.UtcNow.AddDays(90),
        false,
        Granted,
        $"{tag}-unit",
        null);

    private CosmosAccessService AccessService(IMachineAccessService? machineAccessService = null) => new(
        Repositories,
        SerializerOptions,
        machineAccessService);

    // Fresh stores, fresh caches: every answer comes from Cosmos DB, so a cached value can't hide a leak.
    private CosmosFederatedIssuerStore Issuers() => new(
        Repositories,
        SerializerOptions,
        new MemoryCache(new MemoryCacheOptions()));

    private CosmosSharedCertificateStore SharedCertificates() => new(
        Repositories,
        SerializerOptions,
        new MemoryCache(new MemoryCacheOptions()));

    private CosmosClientCertificateStore ClientCertificates() => new(
        Repositories,
        Context.Services.GetRequiredService<IOptions<Newtonsoft.Json.JsonSerializerSettings>>(),
        SerializerOptions,
        new MemoryCache(new MemoryCacheOptions()));

    private CosmosCertificateRenewalService Renewal(IClientCertificateAuthority authority) => new(
        Repositories,
        authority,
        Options.Create(new MachineAuthenticationOptions()),
        SerializerOptions,
        NullLogger<CosmosCertificateRenewalService>.Instance);

    // A machine created through the access service, with the id the test chooses, so both organizations can have it,
    // and values that carry the tag of its organization ("a" or "b"). A machine with a thumbprint has a certificate that
    // expires in 10 days, inside the default renewal window of 30.
    private async Task CreateMachineAsync(
        string organization,
        string project,
        string id,
        string tag,
        MachineCredentialKind kind = MachineCredentialKind.ApiKey,
        string? thumbprint = null,
        IReadOnlyList<FederationRule>? rules = null)
    {
        var machine = new MachineAccess
        {
            Id = id,
            ObjectId = id,
            AccessKey = $"{tag}-key-1",
            Scopes = Granted,
            AnnotationKey = $"{tag}-unit",
            CredentialKind = kind,
            CertificateThumbprint = thumbprint,
            CertificateLifetimeDays = thumbprint is null ? null : 90,
            CertificateNotAfter = thumbprint is null ? null : DateTimeOffset.UtcNow.AddDays(10),
            FederationRules = rules,
        };

        await AccessService(new TaggedMachineAccessService(HashStore, tag, machine)).CreateMachineAccessAsync(new MachineAccessCreate
        {
            Organization = organization,
            Project = project,
            Scopes = Granted,
            AnnotationKey = machine.AnnotationKey,
            CredentialKind = kind,
            FederationRules = rules,
        });
    }

    // The Organization of the stored document, read with the full key of the organization; null when it has no such item.
    private async Task<string?> StoredOrganizationAsync(string organization, string partition, string id)
    {
        var repository = Repositories.GetOrganizationContainer(organization);
        using var response = await repository.Container.ReadItemStreamAsync(id, repository.Key(partition));

        if (response.StatusCode == HttpStatusCode.NotFound)
        {
            return null;
        }

        response.EnsureSuccessStatusCode();
        using var document = await JsonDocument.ParseAsync(response.Content);
        return document.RootElement.GetProperty("Organization").GetString();
    }

    // The ETag of a stored item: the same ETag after an operation means nothing wrote the item meanwhile.
    private async Task<string> ETagAsync(string organization, string partition, string id)
    {
        var repository = Repositories.GetOrganizationContainer(organization);
        using var response = await repository.Container.ReadItemStreamAsync(id, repository.Key(partition));
        response.StatusCode.Should().Be(HttpStatusCode.OK, $"{organization} has the item {id}");
        return response.Headers.ETag;
    }

    // The API key service in short, over the Cosmos hash store: it creates the machine the test gives it (the hash
    // first, as the real one does) and resets and deletes hashes with the tag of its organization.
    private sealed class TaggedMachineAccessService(IApiKeyHashStore hashStore, string tag, MachineAccess? created = null) : IMachineAccessService
    {
        public async Task<MachineAccess> CreateMachineAccessAsync(MachineAccessCreate model)
        {
            var machine = created ?? throw new InvalidOperationException("The test gave no machine to create.");

            if (machine.CredentialKind.HasApiKey())
            {
                await hashStore.StoreAsync(model.Organization, model.Project, machine.Id, $"{tag}-hash-1", machine.Scopes);
            }

            return machine;
        }

        public async Task<string?> ResetMachineAccessAsync(string objectId, string organization, string project)
        {
            var existing = await hashStore.GetAsync(organization, project, objectId);

            if (existing is null)
            {
                return null;
            }

            return await hashStore.ReplaceAsync(organization, project, objectId, existing.HashedSecret, $"{tag}-hash-2")
                ? $"{tag}-key-2"
                : throw new ConflictException($"The API key of the machine access {objectId} was reset concurrently.");
        }

        public Task<bool> DeleteMachineAccessAsync(string objectId, string organization, string project)
        {
            return hashStore.DeleteAsync(organization, project, objectId);
        }
    }
}
