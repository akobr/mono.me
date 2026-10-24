using _42.Platform.Storyteller.Mcp.Tools;
using _42.Platform.Storyteller.Mcp.UnitTests.Support;

using Moq;

namespace _42.Platform.Storyteller.Mcp.UnitTests;

public class KeyValueToolsTests
{
    private readonly Mock<Sdk.IKeyValuesApiClient> _keyValues = new(MockBehavior.Strict);

    [Fact]
    public async Task GetKeyValue_NeverReveals()
    {
        _keyValues.Setup(api => api.GetKeyValueAsync("house", "main", "default", "db.password", false, true, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Sdk.KeyValue { Key = "db.password", Protection = Sdk.KeyValueProtection.Standard, IsSecret = true, Version = 3 });
        _keyValues.Setup(api => api.GetKeyValueVersionAsync("house", "main", "default", "db.password", 2, false, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Sdk.KeyValue { Key = "db.password", Protection = Sdk.KeyValueProtection.Standard, IsSecret = true, Version = 2 });

        var current = await Tools().GetKeyValue("house", "main", "db.password", includeDeleted: true);
        var older = await Tools().GetKeyValue("house", "main", "db.password", version: 2);

        current.StructuredContent!.Value.GetProperty("Version").GetInt64().ShouldBe(3);
        older.StructuredContent!.Value.GetProperty("Version").GetInt64().ShouldBe(2);
        current.StructuredContent!.Value.GetProperty(KeyValueProtections.ClientEncryptedProperty).GetBoolean().ShouldBeFalse();
        _keyValues.Verify(api => api.GetKeyValueAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), true, It.IsAny<bool?>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task GetKeyValue_Hyok_IsMarkedClientEncrypted()
    {
        _keyValues.Setup(api => api.GetKeyValueAsync("house", "main", "default", "api.token", false, null, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Sdk.KeyValue { Key = "api.token", Protection = Sdk.KeyValueProtection.Hyok, IsSecret = true, Recipients = ["worker"] });

        var result = await Tools().GetKeyValue("house", "main", "api.token");

        var content = result.StructuredContent!.Value;
        content.GetProperty(KeyValueProtections.ClientEncryptedProperty).GetBoolean().ShouldBeTrue();
        content.GetProperty("Protection").GetString().ShouldBe("Hyok");
        content.TryGetProperty("Value", out _).ShouldBeFalse();
    }

    [Theory]
    [InlineData("Hyok")]
    [InlineData("hyok")]
    public async Task SetKeyValue_RefusesHyok(string protection)
    {
        var exception = await Should.ThrowAsync<ToolInputException>(() => Tools().SetKeyValue("house", "main", "api.token", "{\"protected\":\"...\"}", protection: protection));

        exception.Message.ShouldBe(KeyValueProtections.HyokRefusal);
        exception.Message.ShouldContain("sform");
        _keyValues.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task SetKeyValue_SendsTheWrite()
    {
        Sdk.KeyValueWrite? sent = null;
        _keyValues.Setup(api => api.SetKeyValueAsync("house", "main", "staging", "db.host", It.IsAny<Sdk.KeyValueWrite>(), It.IsAny<CancellationToken>()))
            .Callback<string, string, string, string, Sdk.KeyValueWrite, CancellationToken>((_, _, _, _, body, _) => sent = body)
            .ReturnsAsync(new Sdk.KeyValue { Key = "db.host", Version = 1 });

        await Tools().SetKeyValue("house", "main", "db.host", string.Empty, view: "staging", protection: "public", contentType: " text/plain ", expectedVersion: 0);

        sent.ShouldNotBeNull();
        sent.Value.ShouldBe(string.Empty);
        sent.Protection.ShouldBe(Sdk.KeyValueWriteProtection.Public);
        sent.ContentType.ShouldBe("text/plain");
        sent.ExpectedVersion.ShouldBe(0);
    }

    [Fact]
    public async Task SetKeyValue_ExistingHyokPairWithoutProtection_IsRefusedBeforeTheWrite()
    {
        _keyValues.Setup(api => api.GetKeyValueAsync("house", "main", "default", "api.token", false, true, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Sdk.KeyValue { Key = "api.token", Protection = Sdk.KeyValueProtection.Hyok, IsSecret = true, Version = 2 });

        var exception = await Should.ThrowAsync<ToolInputException>(() => Tools().SetKeyValue("house", "main", "api.token", "new-plaintext-secret"));

        exception.Message.ShouldBe(KeyValueProtections.HyokRefusal);
        _keyValues.Verify(api => api.SetKeyValueAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<Sdk.KeyValueWrite>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task SetKeyValue_WithoutProtection_WritesANewKeyOrAPairThatIsNotHyok(bool exists)
    {
        var read = _keyValues.Setup(api => api.GetKeyValueAsync("house", "main", "default", "db.host", false, true, It.IsAny<CancellationToken>()));

        if (exists)
        {
            read.ReturnsAsync(new Sdk.KeyValue { Key = "db.host", Protection = Sdk.KeyValueProtection.Standard, IsSecret = true, Version = 2 });
        }
        else
        {
            read.ThrowsAsync(ToolFixtures.NotFound());
        }

        Sdk.KeyValueWrite? sent = null;
        _keyValues.Setup(api => api.SetKeyValueAsync("house", "main", "default", "db.host", It.IsAny<Sdk.KeyValueWrite>(), It.IsAny<CancellationToken>()))
            .Callback<string, string, string, string, Sdk.KeyValueWrite, CancellationToken>((_, _, _, _, body, _) => sent = body)
            .ReturnsAsync(new Sdk.KeyValue { Key = "db.host", Version = 3 });

        await Tools().SetKeyValue("house", "main", "db.host", "sql.internal");

        sent.ShouldNotBeNull();
        sent.Protection.ShouldBeNull();
        sent.Value.ShouldBe("sql.internal");
    }

    [Fact]
    public void SetKeyValue_ContentTypeDescription_SaysAnUpdateWithoutItClearsTheType()
    {
        var parameter = typeof(KeyValueTools).GetMethod(nameof(KeyValueTools.SetKeyValue))!.GetParameters().Single(item => item.Name == "contentType");
        var description = parameter.GetCustomAttributes(typeof(System.ComponentModel.DescriptionAttribute), false)
            .Cast<System.ComponentModel.DescriptionAttribute>()
            .Single()
            .Description;

        description.ShouldContain("an update without it clears the stored type");
    }

    [Fact]
    public async Task SetKeyValue_UnknownProtection_IsAnInputError()
    {
        var exception = await Should.ThrowAsync<ToolInputException>(() => Tools().SetKeyValue("house", "main", "db.host", "x", protection: "Secret"));

        exception.Message.ShouldContain("Public, Standard, Hsm, Byok or Hyok");
        _keyValues.VerifyNoOtherCalls();
    }

    [Theory]
    [InlineData(2, null)]
    [InlineData(3, 2)]
    [InlineData(1, 2)]
    public async Task DiffKeyValue_SecretVersion_IsRefusedWithoutCallingTheDiff(int version, int? versionFrom)
    {
        // Version 2 is a secret: compared directly, as the previous version or as versionFrom.
        SetupVersions((1, Sdk.KeyValueVersionProtection.Public), (2, Sdk.KeyValueVersionProtection.Standard), (3, Sdk.KeyValueVersionProtection.Public));

        var result = await Tools().DiffKeyValue("house", "main", "db.password", version, versionFrom: versionFrom);

        result.IsError.ShouldBe(true);
        result.Text().ShouldContain("Version 2 of 'db.password' is a secret (Standard)");
        result.Text().ShouldContain("sform story kv diff db.password");
        result.StructuredContent!.Value.GetProperty("Protection").GetString().ShouldBe("Standard");
        VerifyNoDiff();
    }

    [Fact]
    public async Task DiffKeyValue_Hyok_IsNotComparable()
    {
        SetupVersions((1, Sdk.KeyValueVersionProtection.Hyok), (2, Sdk.KeyValueVersionProtection.Hyok));

        var result = await Tools().DiffKeyValue("house", "main", "api.token", 2);

        result.IsError.ShouldBe(true);
        result.Text().ShouldContain("client-encrypted (Hyok)");
        VerifyNoDiff();
    }

    [Fact]
    public async Task DiffKeyValue_VersionNotStored_IsAnInputError()
    {
        SetupVersions((5, Sdk.KeyValueVersionProtection.Public));

        var exception = await Should.ThrowAsync<ToolInputException>(() => Tools().DiffKeyValue("house", "main", "db.host", 5));

        exception.Message.ShouldContain("Version 4 of 'db.host' is not stored");
        VerifyNoDiff();
    }

    [Fact]
    public async Task DiffKeyValue_PublicVersions_AreComparedAsJson()
    {
        SetupVersions((1, Sdk.KeyValueVersionProtection.Public), (2, Sdk.KeyValueVersionProtection.Public));
        _keyValues.Setup(api => api.GetKeyValueVersionDiffAsync("house", "main", "default", "db.host", 1, null, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Sdk.DiffResult { Hunks = [] });
        _keyValues.Setup(api => api.GetKeyValueVersionDiffCustomAsync("house", "main", "default", "db.host", 2, 0, null, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Sdk.DiffResult { Hunks = [] });

        var first = await Tools().DiffKeyValue("house", "main", "db.host", 1);
        var custom = await Tools().DiffKeyValue("house", "main", "db.host", 2, versionFrom: 0);

        first.IsError.ShouldNotBe(true);
        custom.IsError.ShouldNotBe(true);
        _keyValues.Verify(api => api.GetKeyValueVersionDiffAsync("house", "main", "default", "db.host", 1, null, It.IsAny<CancellationToken>()), Times.Once);
        _keyValues.Verify(api => api.GetKeyValueVersionDiffCustomAsync("house", "main", "default", "db.host", 2, 0, null, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task ListKeyValues_PagesAcrossApiPagesAndPassesTheFilters()
    {
        var page1 = new Sdk.KeyValuesResponse { Items = Enumerable.Range(0, 3).Select(index => new Sdk.KeyValueSummary { Key = $"k{index}" }).ToList(), ContinuationToken = "api-2" };
        var page2 = new Sdk.KeyValuesResponse { Items = [new Sdk.KeyValueSummary { Key = "k3" }] };
        _keyValues.Setup(api => api.GetKeyValuesAsync("house", "main", "default", "db.", "Public,Hsm", "Deleted", true, null, It.IsAny<CancellationToken>())).ReturnsAsync(page1);
        _keyValues.Setup(api => api.GetKeyValuesAsync("house", "main", "default", "db.", "Public,Hsm", "Deleted", true, "api-2", It.IsAny<CancellationToken>())).ReturnsAsync(page2);

        var keys = new List<string>();
        string? token = null;

        do
        {
            var result = await Tools().ListKeyValues("house", "main", keyPrefix: "db.", protections: ["public", "HSM", "Public"], state: "deleted", includeValues: true, limit: 2, continuationToken: token);
            var content = result.StructuredContent!.Value;
            keys.AddRange(content.GetProperty("Items").EnumerateArray().Select(item => item.GetProperty("Key").GetString()!));
            token = content.TryGetProperty("ContinuationToken", out var next) ? next.GetString() : null;
        }
        while (token is not null);

        keys.ShouldBe(["k0", "k1", "k2", "k3"]);
    }

    [Fact]
    public async Task ListKeyValues_UnknownState_IsAnInputError()
    {
        await Should.ThrowAsync<ToolInputException>(() => Tools().ListKeyValues("house", "main", state: "Gone"));

        _keyValues.VerifyNoOtherCalls();
    }

    [Theory]
    [InlineData(new[] { "a" }, "db.")]
    [InlineData(null, null)]
    [InlineData(new string[0], null)]
    public async Task BulkTools_NeedKeysOrAPrefix(string[]? keys, string? keyPrefix)
    {
        await Should.ThrowAsync<ToolInputException>(() => Tools().RestoreKeyValues("house", "main", keys: keys, keyPrefix: keyPrefix));
        await Should.ThrowAsync<ToolInputException>(() => Tools().PurgeKeyValues("house", "main", keys: keys, keyPrefix: keyPrefix));
        await Should.ThrowAsync<ToolInputException>(() => Tools().ProtectKeyValues("house", "main", "Hsm", keys: keys, keyPrefix: keyPrefix));

        _keyValues.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task BulkTools_TakeAtMostAHundredKeys()
    {
        var keys = Enumerable.Range(0, KeyValueTools.MaxBulkKeys + 1).Select(index => $"k{index}").ToArray();

        var exception = await Should.ThrowAsync<ToolInputException>(() => Tools().RestoreKeyValues("house", "main", keys: keys));

        exception.Message.ShouldContain("at most 100");
        _keyValues.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task PurgeKeyValues_EmptyPrefix_IsEveryDeletedKey()
    {
        Sdk.KeyValueBulkRequest? sent = null;
        _keyValues.Setup(api => api.PurgeKeyValuesAsync("house", "main", "default", It.IsAny<Sdk.KeyValueBulkRequest>(), It.IsAny<CancellationToken>()))
            .Callback<string, string, string, Sdk.KeyValueBulkRequest, CancellationToken>((_, _, _, body, _) => sent = body)
            .ReturnsAsync(new Sdk.KeyValueBulkResult { Items = [] });

        await Tools().PurgeKeyValues("house", "main", keyPrefix: string.Empty);

        sent.ShouldNotBeNull();
        sent.KeyPrefix.ShouldBe(string.Empty);
        sent.Keys.ShouldBeNull();
    }

    [Fact]
    public async Task ProtectKeyValues_SendsTheTargetLevel()
    {
        Sdk.KeyValueBulkRequest? sent = null;
        _keyValues.Setup(api => api.ProtectKeyValuesAsync("house", "main", "default", It.IsAny<Sdk.KeyValueBulkRequest>(), It.IsAny<CancellationToken>()))
            .Callback<string, string, string, Sdk.KeyValueBulkRequest, CancellationToken>((_, _, _, body, _) => sent = body)
            .ReturnsAsync(new Sdk.KeyValueBulkResult { Items = [new Sdk.KeyValueBulkItemResult { Key = "db.password", Outcome = Sdk.KeyValueBulkItemResultOutcome.Done, Version = 4 }] });

        var result = await Tools().ProtectKeyValues("house", "main", "hsm", keys: ["db.password", "DB.Password"]);

        sent.ShouldNotBeNull();
        sent.Protection.ShouldBe(Sdk.KeyValueBulkRequestProtection.Hsm);
        sent.Keys.ShouldBe(["db.password"]);
        result.Text().ShouldContain("\"Outcome\":\"Done\"");
    }

    [Theory]
    [InlineData("Public")]
    [InlineData("Hyok")]
    public async Task ProtectKeyValues_RefusesLevelsTheServerCannotMoveTo(string protection)
    {
        await Should.ThrowAsync<ToolInputException>(() => Tools().ProtectKeyValues("house", "main", protection, keyPrefix: "db."));

        _keyValues.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task DeleteKeyValue_SoftDeleteSendsNoPermanentFlag()
    {
        _keyValues.Setup(api => api.DeleteKeyValueAsync("house", "main", "default", "db.host", null, 3, It.IsAny<CancellationToken>())).Returns(Task.CompletedTask);
        _keyValues.Setup(api => api.DeleteKeyValueAsync("house", "main", "default", "db.port", true, null, It.IsAny<CancellationToken>())).Returns(Task.CompletedTask);

        var soft = await Tools().DeleteKeyValue("house", "main", "db.host", expectedVersion: 3);
        var permanent = await Tools().DeleteKeyValue("house", "main", "db.port", permanent: true);

        soft.StructuredContent!.Value.GetProperty("Permanent").GetBoolean().ShouldBeFalse();
        permanent.StructuredContent!.Value.GetProperty("Permanent").GetBoolean().ShouldBeTrue();
    }

    [Fact]
    public async Task SetKeyValuePolicy_SendsTheChangedMembersOnly()
    {
        Sdk.KeyValuePolicyUpdate? sent = null;
        _keyValues.Setup(api => api.SetKeyValuePolicyAsync("house", "main", It.IsAny<Sdk.KeyValuePolicyUpdate>(), It.IsAny<CancellationToken>()))
            .Callback<string, string, Sdk.KeyValuePolicyUpdate, CancellationToken>((_, _, body, _) => sent = body)
            .ReturnsAsync(new Sdk.KeyValuePolicy { Version = 8 });

        await Tools().SetKeyValuePolicy("house", "main", allowedProtections: ["Public", "standard", "Hsm"], hsmRotationDays: 90, expectedVersion: 7);

        sent.ShouldNotBeNull();
        sent.AllowedProtections.ShouldBe([Sdk.AllowedProtections2.Public, Sdk.AllowedProtections2.Standard, Sdk.AllowedProtections2.Hsm]);
        sent.HsmRotationInterval.ShouldBe("90.00:00:00");
        sent.DefaultSecretProtection.ShouldBeNull();
        sent.RequireMtlsForSecretDelivery.ShouldBeNull();
        sent.Byok.ShouldBeNull();
        sent.ExpectedVersion.ShouldBe(7);
    }

    [Fact]
    public async Task SetKeyValuePolicy_ListWithoutPublic_GetsPublicAdded()
    {
        Sdk.KeyValuePolicyUpdate? sent = null;
        _keyValues.Setup(api => api.SetKeyValuePolicyAsync("house", "main", It.IsAny<Sdk.KeyValuePolicyUpdate>(), It.IsAny<CancellationToken>()))
            .Callback<string, string, Sdk.KeyValuePolicyUpdate, CancellationToken>((_, _, body, _) => sent = body)
            .ReturnsAsync(new Sdk.KeyValuePolicy { Version = 8 });

        await Tools().SetKeyValuePolicy("house", "main", allowedProtections: ["Standard", "Hsm"]);

        sent.ShouldNotBeNull();
        sent.AllowedProtections.ShouldBe([Sdk.AllowedProtections2.Standard, Sdk.AllowedProtections2.Hsm, Sdk.AllowedProtections2.Public]);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(3651)]
    [InlineData(int.MaxValue)]
    public async Task SetKeyValuePolicy_HsmRotationOutsideTheApiBounds_IsAnInputError(int days)
    {
        var exception = await Should.ThrowAsync<ToolInputException>(() => Tools().SetKeyValuePolicy("house", "main", hsmRotationDays: days));

        exception.Message.ShouldBe("'hsmRotationDays' is 1 to 3,650.");
        _keyValues.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task SetKeyValuePolicy_HsmRotationOfTenYears_IsSent()
    {
        Sdk.KeyValuePolicyUpdate? sent = null;
        _keyValues.Setup(api => api.SetKeyValuePolicyAsync("house", "main", It.IsAny<Sdk.KeyValuePolicyUpdate>(), It.IsAny<CancellationToken>()))
            .Callback<string, string, Sdk.KeyValuePolicyUpdate, CancellationToken>((_, _, body, _) => sent = body)
            .ReturnsAsync(new Sdk.KeyValuePolicy { Version = 8 });

        await Tools().SetKeyValuePolicy("house", "main", hsmRotationDays: 3650);

        sent.ShouldNotBeNull();
        sent.HsmRotationInterval.ShouldBe("3650.00:00:00");
    }

    [Fact]
    public async Task SetKeyValuePolicy_NothingToChange_IsAnInputError()
    {
        await Should.ThrowAsync<ToolInputException>(() => Tools().SetKeyValuePolicy("house", "main", expectedVersion: 7));
        await Should.ThrowAsync<ToolInputException>(() => Tools().SetKeyValuePolicy("house", "main", allowedProtections: []));

        _keyValues.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task GetKeyValueAudit_SendsTimesInUtcAndPagesInTheHost()
    {
        DateTimeOffset? sentFrom = null;
        _keyValues.Setup(api => api.GetKeyValueAuditAsync("house", "main", "default", null, "Reveal", It.IsAny<DateTimeOffset?>(), null, null, It.IsAny<CancellationToken>()))
            .Callback<string, string, string, string, string, DateTimeOffset?, DateTimeOffset?, string, CancellationToken>((_, _, _, _, _, from, _, _, _) => sentFrom = from)
            .ReturnsAsync(new Sdk.KeyValueAuditResponse { Items = [new Sdk.KeyValueAuditEvent { Id = "e1" }, new Sdk.KeyValueAuditEvent { Id = "e2" }] });

        var result = await Tools().GetKeyValueAudit("house", "main", filterView: "default", operation: "reveal", from: "2026-10-24T12:00:00+02:00", limit: 1);

        sentFrom.ShouldBe(new DateTimeOffset(2026, 10, 24, 10, 0, 0, TimeSpan.Zero));
        sentFrom!.Value.Offset.ShouldBe(TimeSpan.Zero);
        result.StructuredContent!.Value.GetProperty("Count").GetInt32().ShouldBe(1);
        result.StructuredContent!.Value.TryGetProperty("ContinuationToken", out _).ShouldBeTrue();
    }

    [Theory]
    [InlineData("yesterday", null)]
    [InlineData(null, "2026-13-01")]
    public async Task GetKeyValueUsage_BadDay_IsAnInputError(string? from, string? to)
    {
        await Should.ThrowAsync<ToolInputException>(() => Tools().GetKeyValueUsage("house", "main", from, to));

        _keyValues.VerifyNoOtherCalls();
    }

    private void SetupVersions(params (long Version, Sdk.KeyValueVersionProtection Protection)[] versions)
    {
        _keyValues.Setup(api => api.GetKeyValueVersionsAsync("house", "main", "default", It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(versions.Select(item => new Sdk.KeyValueVersion { Version = item.Version, Protection = item.Protection }).ToList());
    }

    private void VerifyNoDiff()
    {
        _keyValues.Verify(api => api.GetKeyValueVersionDiffAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<int>(), It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
        _keyValues.Verify(api => api.GetKeyValueVersionDiffCustomAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<int>(), It.IsAny<int>(), It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    private KeyValueTools Tools() => new(_keyValues.Object);
}
