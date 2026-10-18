using _42.Platform.Storyteller.Transferring;

namespace _42.Platform.Storyteller.Backend.Core.UnitTests.Transferring;

public class ViewArchiveNamesTests
{
    [Theory]
    [InlineData("rst.payments", "rst.payments.json")]
    [InlineData("usg.billing.payments", "usg.billing.payments.json")]
    [InlineData("exe.northwind.payments.prod", "exe.northwind.payments.prod.json")]
    [InlineData("uxe.billing.payments.prod.nightly", "uxe.billing.payments.prod.nightly.json")]
    public void GetAnnotationFileName_IsTheKey(string key, string expected)
    {
        ViewArchiveNames.GetAnnotationFileName(key).ShouldBe(expected);
    }

    [Theory]
    [InlineData(null, "rst.payments.conf.json")]
    [InlineData(3UL, "rst.payments.conf.3.json")]
    public void GetConfigurationFileName_HasTheConfSuffix(ulong? version, string expected)
    {
        ViewArchiveNames.GetConfigurationFileName("rst.payments", version).ShouldBe(expected);
    }

    [Theory]
    [InlineData("exe", null, "exe.template.json")]
    [InlineData("exe", 4UL, "exe.template.4.json")]
    [InlineData("EXE", null, "exe.template.json")]
    public void GetTemplateFileName_IsTheLowerCaseTypeCode(string type, ulong? version, string expected)
    {
        ViewArchiveNames.GetTemplateFileName(type, version).ShouldBe(expected);
    }

    [Theory]
    [InlineData(null, "exe.schema.json")]
    [InlineData(1UL, "exe.schema.1.json")]
    public void GetTypeSchemaFileName_HasTheSchemaSuffix(ulong? version, string expected)
    {
        ViewArchiveNames.GetTypeSchemaFileName("exe", version).ShouldBe(expected);
    }

    [Theory]
    [InlineData(null, "rst.payments.schema.json")]
    [InlineData(2UL, "rst.payments.schema.2.json")]
    public void GetAnnotationSchemaFileName_HasTheSchemaSuffix(ulong? version, string expected)
    {
        ViewArchiveNames.GetAnnotationSchemaFileName("rst.payments", version).ShouldBe(expected);
    }

    [Theory]
    [InlineData("rst.payments", "exe", null, "rst.payments.exe.schema.json")]
    [InlineData("sbt.northwind", "EXE", 2UL, "sbt.northwind.exe.schema.2.json")]
    public void GetDescendantTypeSchemaFileName_HasTheTypeBeforeTheSchemaSuffix(string key, string type, ulong? version, string expected)
    {
        ViewArchiveNames.GetDescendantTypeSchemaFileName(key, type, version).ShouldBe(expected);
    }

    [Theory]
    [InlineData(ViewDocumentKind.Annotation, null, "rst.payments", null, "rst.payments.json")]
    [InlineData(ViewDocumentKind.Configuration, null, "rst.payments", 3UL, "rst.payments.conf.3.json")]
    [InlineData(ViewDocumentKind.Template, "exe", null, 4UL, "exe.template.4.json")]
    [InlineData(ViewDocumentKind.TypeSchema, "exe", null, null, "exe.schema.json")]
    [InlineData(ViewDocumentKind.AnnotationSchema, null, "rst.payments", null, "rst.payments.schema.json")]
    [InlineData(ViewDocumentKind.DescendantTypeSchema, "exe", "sbt.northwind", 2UL, "sbt.northwind.exe.schema.2.json")]
    public void GetFileName_NamesEveryKind(ViewDocumentKind kind, string? type, string? key, ulong? version, string expected)
    {
        ViewArchiveNames.GetFileName(kind, type, key, version).ShouldBe(expected);
    }

    [Fact]
    public void GetFileName_RejectsAVersionOfAnAnnotationAndMissingParts()
    {
        Should.Throw<ArgumentOutOfRangeException>(() => ViewArchiveNames.GetFileName(ViewDocumentKind.Annotation, null, "rst.payments", 1));
        Should.Throw<ArgumentNullException>(() => ViewArchiveNames.GetFileName(ViewDocumentKind.Configuration, null, null));
        Should.Throw<ArgumentNullException>(() => ViewArchiveNames.GetFileName(ViewDocumentKind.DescendantTypeSchema, null, "rst.payments"));
        Should.Throw<ArgumentOutOfRangeException>(() => ViewArchiveNames.GetTemplateFileName("xyz"));
    }

    [Theory]
    [InlineData("rst.a:b", "rst.a%3Ab.json")]
    [InlineData("rst.50%", "rst.50%25.json")]
    [InlineData("sbt.<x>|y?*\"", "sbt.%3Cx%3E%7Cy%3F%2A%22.json")]
    [InlineData("rst.tab\there", "rst.tab%09here.json")]
    [InlineData("rst.café", "rst.café.json")]
    public void GetAnnotationFileName_EncodesCharactersThatFileSystemsReject(string key, string expected)
    {
        ViewArchiveNames.GetAnnotationFileName(key).ShouldBe(expected);
    }

    // The documents of the file-name tables (1.3) and of the archive layout (1.4).
    [Theory]
    [InlineData("rst.payments.json", ViewDocumentKind.Annotation, null, "rst.payments", null)]
    [InlineData("usg.northwind.payments.json", ViewDocumentKind.Annotation, null, "usg.northwind.payments", null)]
    [InlineData("exe.northwind.payments.prod.json", ViewDocumentKind.Annotation, null, "exe.northwind.payments.prod", null)]
    [InlineData("rst.payments.conf.json", ViewDocumentKind.Configuration, null, "rst.payments", null)]
    [InlineData("rst.payments.conf.3.json", ViewDocumentKind.Configuration, null, "rst.payments", 3UL)]
    [InlineData("rst.payments.conf.1.json", ViewDocumentKind.Configuration, null, "rst.payments", 1UL)]
    [InlineData("exe.northwind.payments.prod.conf.json", ViewDocumentKind.Configuration, null, "exe.northwind.payments.prod", null)]
    [InlineData("exe.template.json", ViewDocumentKind.Template, "exe", null, null)]
    [InlineData("exe.template.4.json", ViewDocumentKind.Template, "exe", null, 4UL)]
    [InlineData("exe.template.3.json", ViewDocumentKind.Template, "exe", null, 3UL)]
    [InlineData("exe.schema.json", ViewDocumentKind.TypeSchema, "exe", null, null)]
    [InlineData("exe.schema.1.json", ViewDocumentKind.TypeSchema, "exe", null, 1UL)]
    [InlineData("rst.payments.schema.json", ViewDocumentKind.AnnotationSchema, null, "rst.payments", null)]
    [InlineData("rst.payments.schema.2.json", ViewDocumentKind.AnnotationSchema, null, "rst.payments", 2UL)]
    [InlineData("rst.payments.exe.schema.json", ViewDocumentKind.DescendantTypeSchema, "exe", "rst.payments", null)]
    [InlineData("sbt.northwind.exe.schema.json", ViewDocumentKind.DescendantTypeSchema, "exe", "sbt.northwind", null)]
    [InlineData("sbt.northwind.exe.schema.2.json", ViewDocumentKind.DescendantTypeSchema, "exe", "sbt.northwind", 2UL)]
    [InlineData("rst.%73chema.json", ViewDocumentKind.Annotation, null, "rst.schema", null)]
    public void TryParse_TellsTheDocument(string fileName, ViewDocumentKind kind, string? type, string? key, ulong? version)
    {
        ViewArchiveNames.TryParse(fileName, out var name).ShouldBeTrue();

        name.Kind.ShouldBe(kind);
        name.AnnotationType.ShouldBe(type);
        name.AnnotationKey?.ToString().ShouldBe(key);
        (name.AnnotationKey is null).ShouldBe(key is null);
        name.Version.ShouldBe(version);
        name.ErrorCode.ShouldBeNull();
    }

    [Fact]
    public void TryParse_TakesNoVersionOfAnAnnotation()
    {
        // a version file of PR #88: annotations have no versions
        ViewArchiveNames.TryParse("rst.payments.3.json", out var name).ShouldBeFalse();
        name.ShouldBeNull();

        // a usage has three segments, so "3" is the responsibility's name
        ViewArchiveNames.TryParse("usg.payments.3.json", out name).ShouldBeTrue();
        name.Kind.ShouldBe(ViewDocumentKind.Annotation);
        name.AnnotationKey!.ToString().ShouldBe("usg.payments.3");
    }

    [Theory]
    [InlineData("rst.schema", "rst.%73chema.json")]
    [InlineData("rst.template", "rst.%74emplate.json")]
    [InlineData("sbt.Schema", "sbt.%53chema.json")]
    [InlineData("sbt.TEMPLATE", "sbt.%54EMPLATE.json")]
    [InlineData("unt.template.3", "unt.%74emplate.3.json")]
    [InlineData("usg.schema.payments", "usg.%73chema.payments.json")]
    public void ReservedSecondSegment_IsEncodedAndDecoded(string key, string fileName)
    {
        ViewArchiveNames.GetAnnotationFileName(key).ShouldBe(fileName);

        ViewArchiveNames.TryParse(fileName, out var name).ShouldBeTrue();
        name.Kind.ShouldBe(ViewDocumentKind.Annotation);
        name.AnnotationKey!.ToString().ShouldBe(key);
    }

    [Fact]
    public void ReservedSecondSegment_IsEncodedForEveryKindOfTheKey()
    {
        ViewArchiveNames.GetConfigurationFileName("rst.schema", 2).ShouldBe("rst.%73chema.conf.2.json");
        ViewArchiveNames.GetAnnotationSchemaFileName("rst.template").ShouldBe("rst.%74emplate.schema.json");
        ViewArchiveNames.GetDescendantTypeSchemaFileName("sbt.schema", "exe").ShouldBe("sbt.%73chema.exe.schema.json");

        ViewArchiveNames.TryParse("rst.%73chema.conf.2.json", out var configuration).ShouldBeTrue();
        configuration.Kind.ShouldBe(ViewDocumentKind.Configuration);
        configuration.AnnotationKey!.ToString().ShouldBe("rst.schema");
        configuration.Version.ShouldBe(2UL);

        ViewArchiveNames.TryParse("sbt.%73chema.exe.schema.json", out var descendant).ShouldBeTrue();
        descendant.Kind.ShouldBe(ViewDocumentKind.DescendantTypeSchema);
        descendant.AnnotationKey!.ToString().ShouldBe("sbt.schema");
        descendant.AnnotationType.ShouldBe("exe");
    }

    [Theory]
    [InlineData("rst.schemas", "rst.schemas.json")]
    [InlineData("rst.myschema", "rst.myschema.json")]
    [InlineData("usg.payments.schema", "usg.payments.schema.json")]
    [InlineData("exe.s.template.c", "exe.s.template.c.json")]
    public void OnlyTheSecondSegmentIsReserved(string key, string fileName)
    {
        ViewArchiveNames.GetAnnotationFileName(key).ShouldBe(fileName);

        ViewArchiveNames.TryParse(fileName, out var name).ShouldBeTrue();
        name.Kind.ShouldBe(ViewDocumentKind.Annotation);
        name.AnnotationKey!.ToString().ShouldBe(key);
    }

    [Fact]
    public void LiteralReservedSegment_IsAFileOfTheType()
    {
        // not encoded, so these are the type's files, not the annotations rst.schema and unt.template.3
        ViewArchiveNames.TryParse("rst.schema.json", out var schema).ShouldBeTrue();
        schema.Kind.ShouldBe(ViewDocumentKind.TypeSchema);
        schema.AnnotationType.ShouldBe("rst");
        schema.AnnotationKey.ShouldBeNull();

        ViewArchiveNames.TryParse("unt.template.3.json", out var template).ShouldBeTrue();
        template.Kind.ShouldBe(ViewDocumentKind.Template);
        template.AnnotationType.ShouldBe("unt");
        template.Version.ShouldBe(3UL);
    }

    [Theory]
    [InlineData("rst.a:b")]
    [InlineData("rst.50%")]
    [InlineData("sbt.<x>|y?*\"")]
    [InlineData("cnt.subject.prod")]
    [InlineData("exe.s.r.c")]
    [InlineData("uxe.s.r.c.u")]
    [InlineData("unt.r.u%25")]
    [InlineData("rst.schema")]
    [InlineData("unt.Template.3")]
    public void TryParse_ReversesTheBuildersOfKeys(string key)
    {
        Parse(ViewArchiveNames.GetAnnotationFileName(key)).ShouldBe((ViewDocumentKind.Annotation, (string?)null, key, (ulong?)null));
        Parse(ViewArchiveNames.GetConfigurationFileName(key)).ShouldBe((ViewDocumentKind.Configuration, (string?)null, key, (ulong?)null));
        Parse(ViewArchiveNames.GetConfigurationFileName(key, 7)).ShouldBe((ViewDocumentKind.Configuration, (string?)null, key, (ulong?)7));
        Parse(ViewArchiveNames.GetAnnotationSchemaFileName(key)).ShouldBe((ViewDocumentKind.AnnotationSchema, (string?)null, key, (ulong?)null));
        Parse(ViewArchiveNames.GetAnnotationSchemaFileName(key, 2)).ShouldBe((ViewDocumentKind.AnnotationSchema, (string?)null, key, (ulong?)2));
    }

    [Theory]
    [InlineData("rst")]
    [InlineData("sbt")]
    [InlineData("usg")]
    [InlineData("cnt")]
    [InlineData("exe")]
    [InlineData("unt")]
    [InlineData("uxe")]
    public void TryParse_ReversesTheBuildersOfTypes(string type)
    {
        Parse(ViewArchiveNames.GetTemplateFileName(type)).ShouldBe((ViewDocumentKind.Template, type, (string?)null, (ulong?)null));
        Parse(ViewArchiveNames.GetTemplateFileName(type, 5)).ShouldBe((ViewDocumentKind.Template, type, (string?)null, (ulong?)5));
        Parse(ViewArchiveNames.GetTypeSchemaFileName(type)).ShouldBe((ViewDocumentKind.TypeSchema, type, (string?)null, (ulong?)null));
        Parse(ViewArchiveNames.GetTypeSchemaFileName(type, 9)).ShouldBe((ViewDocumentKind.TypeSchema, type, (string?)null, (ulong?)9));
    }

    [Theory]
    [InlineData("rst.payments", "unt")]
    [InlineData("rst.payments", "usg")]
    [InlineData("rst.payments", "exe")]
    [InlineData("sbt.shop", "cnt")]
    [InlineData("sbt.shop", "uxe")]
    [InlineData("usg.shop.payments", "exe")]
    [InlineData("cnt.shop.prod", "uxe")]
    [InlineData("exe.shop.payments.prod", "uxe")]
    [InlineData("unt.payments.nightly", "uxe")]
    public void TryParse_ReversesTheBuilderOfDescendantTypeSchemas(string key, string type)
    {
        Parse(ViewArchiveNames.GetDescendantTypeSchemaFileName(key, type)).ShouldBe((ViewDocumentKind.DescendantTypeSchema, type, key, (ulong?)null));
        Parse(ViewArchiveNames.GetDescendantTypeSchemaFileName(key, type, 3)).ShouldBe((ViewDocumentKind.DescendantTypeSchema, type, key, (ulong?)3));
    }

    [Theory]
    [InlineData("export.json")]
    [InlineData("README.md")]
    [InlineData(".json")]
    [InlineData("rst.json")]
    [InlineData("rst.payments.extra.json")]
    [InlineData("rst.payments.extra.more.json")]
    [InlineData("rst.payments.template.json")]
    [InlineData("rst.payments.conf.x.json")]
    [InlineData("rst.payments.conf.1.2.json")]
    [InlineData("rst.payments.conf.-1.json")]
    [InlineData("rst.payments.schema.x.json")]
    [InlineData("rst.payments.exe.conf.json")]
    [InlineData("rst.payments.xyz.schema.json")]
    [InlineData("rst.payments.exe.schema.x.json")]
    [InlineData("rst.payments.exe.schema.1.2.json")]
    [InlineData("exe.template.x.json")]
    [InlineData("exe.template.1.2.json")]
    [InlineData("exe.schema.abc.json")]
    [InlineData("exe.s.r.json")]
    [InlineData("xyz.payments.json")]
    [InlineData("xyz.template.json")]
    [InlineData("rst..json")]
    [InlineData("rst.payments..json")]
    [InlineData("rst.payments.-1.json")]
    [InlineData("rst.payments.json.bak")]
    [InlineData("rst.bad%2.json")]
    [InlineData("rst.bad%ZZ.json")]
    [InlineData("rst.a%2Eb.json")]
    public void TryParse_RejectsNamesThatAreNotDocuments(string fileName)
    {
        ViewArchiveNames.TryParse(fileName, out var name).ShouldBeFalse();
        name.ShouldBeNull();
    }

    [Theory]
    [InlineData("exe.s.r.c.rst.schema.json", "rst", "exe.s.r.c")]
    [InlineData("rst.payments.rst.schema.json", "rst", "rst.payments")]
    [InlineData("rst.payments.cnt.schema.json", "cnt", "rst.payments")]
    [InlineData("unt.payments.nightly.exe.schema.3.json", "exe", "unt.payments.nightly")]
    [InlineData("uxe.s.r.c.u.uxe.schema.json", "uxe", "uxe.s.r.c.u")]
    public void TryParse_ReportsADescendantTypeThatCannotDescendFromTheKey(string fileName, string type, string key)
    {
        // still a document, so the import reports it instead of ignoring it
        ViewArchiveNames.TryParse(fileName, out var name).ShouldBeTrue();

        name.Kind.ShouldBe(ViewDocumentKind.DescendantTypeSchema);
        name.AnnotationType.ShouldBe(type);
        name.AnnotationKey!.ToString().ShouldBe(key);
        name.ErrorCode.ShouldBe(ErrorCodes.InvalidKey);
        name.Message.ShouldBe($"The type {type} cannot descend from {key}, so it has no descendant-type schema there.");
    }

    [Theory]
    [InlineData("RST.payments.json", ViewDocumentKind.Annotation, null, "rst.payments", null)]
    [InlineData("rst.payments.JSON", ViewDocumentKind.Annotation, null, "rst.payments", null)]
    [InlineData("rst.payments.CONF.json", ViewDocumentKind.Configuration, null, "rst.payments", null)]
    [InlineData("Rst.payments.Conf.2.Json", ViewDocumentKind.Configuration, null, "rst.payments", 2UL)]
    [InlineData("EXE.Template.JSON", ViewDocumentKind.Template, "exe", null, null)]
    [InlineData("exe.SCHEMA.2.json", ViewDocumentKind.TypeSchema, "exe", null, 2UL)]
    [InlineData("rst.payments.Schema.json", ViewDocumentKind.AnnotationSchema, null, "rst.payments", null)]
    [InlineData("rst.payments.EXE.Schema.json", ViewDocumentKind.DescendantTypeSchema, "exe", "rst.payments", null)]
    [InlineData("rst.Payments.json", ViewDocumentKind.Annotation, null, "rst.Payments", null)]
    public void TryParse_TakesTypeCodesAndSuffixesInAnyCase(string fileName, ViewDocumentKind kind, string? type, string? key, ulong? version)
    {
        // type codes are lower-cased; the names of a key keep their case
        Parse(fileName).ShouldBe((kind, type, key, version));
    }

    [Fact]
    public void Identity_IsTheSameForEncodedAndPlainSpellings()
    {
        ViewArchiveNames.TryParse("rst.payments.json", out var plain).ShouldBeTrue();
        ViewArchiveNames.TryParse("rst.pay%6Dents.json", out var encoded).ShouldBeTrue();
        ViewArchiveNames.TryParse("RST.payments.json", out var upper).ShouldBeTrue();
        ViewArchiveNames.TryParse("rst.payments.conf.json", out var configuration).ShouldBeTrue();
        ViewArchiveNames.TryParse("rst.payments.conf.4.json", out var version).ShouldBeTrue();
        ViewArchiveNames.TryParse("exe.template.json", out var template).ShouldBeTrue();
        ViewArchiveNames.TryParse("exe.schema.json", out var schema).ShouldBeTrue();

        encoded.Identity.ShouldBe(plain.Identity);
        upper.Identity.ShouldBe(plain.Identity);
        configuration.Identity.ShouldNotBe(plain.Identity);
        version.Identity.ShouldBe(configuration.Identity);
        template.Identity.ShouldNotBe(schema.Identity);
    }

    [Theory]
    [InlineData(ViewDocumentKind.Annotation, ViewArchiveContent.Annotations)]
    [InlineData(ViewDocumentKind.TypeSchema, ViewArchiveContent.Schemas)]
    [InlineData(ViewDocumentKind.DescendantTypeSchema, ViewArchiveContent.Schemas)]
    [InlineData(ViewDocumentKind.AnnotationSchema, ViewArchiveContent.Schemas)]
    [InlineData(ViewDocumentKind.Template, ViewArchiveContent.Templates)]
    [InlineData(ViewDocumentKind.Configuration, ViewArchiveContent.Configurations)]
    public void GetContent_GroupsTheKinds(ViewDocumentKind kind, ViewArchiveContent content)
    {
        ViewArchiveNames.GetContent(kind).ShouldBe(content);
    }

    [Theory]
    [InlineData(".git/HEAD")]
    [InlineData(".git/rst.inside.json")]
    [InlineData("repo/.git/config")]
    [InlineData(".github/workflows/rst.payments.json")]
    [InlineData("export/.vscode/settings.json")]
    [InlineData("annotations/.DS_Store")]
    [InlineData("._rst.payments.json")]
    [InlineData("annotations/._rst.payments.json")]
    [InlineData(".hidden.json")]
    [InlineData("__MACOSX/annotations/rst.payments.json")]
    [InlineData("")]
    public void IsSkippedPath_SkipsHiddenPathsAndMacFolders(string path)
    {
        ViewArchiveNames.IsSkippedPath(path).ShouldBeTrue();
    }

    [Theory]
    [InlineData("rst.payments.json")]
    [InlineData("annotations/rst.payments.json")]
    [InlineData("export/configurations/rst.payments.conf.json")]
    [InlineData("./rst.payments.json")]
    [InlineData("a.b/rst.payments.json")]
    public void IsSkippedPath_KeepsOtherPaths(string path)
    {
        ViewArchiveNames.IsSkippedPath(path).ShouldBeFalse();
    }

    [Theory]
    [InlineData(ViewDocumentKind.Annotation, null, "rst.payments", null, "annotations/rst.payments.json")]
    [InlineData(ViewDocumentKind.TypeSchema, "exe", null, 1UL, "schemas/exe.schema.1.json")]
    [InlineData(ViewDocumentKind.DescendantTypeSchema, "exe", "sbt.northwind", null, "schemas/sbt.northwind.exe.schema.json")]
    [InlineData(ViewDocumentKind.AnnotationSchema, null, "rst.schema", null, "schemas/rst.%73chema.schema.json")]
    [InlineData(ViewDocumentKind.Template, "EXE", null, 3UL, "templates/exe.template.3.json")]
    [InlineData(ViewDocumentKind.Configuration, null, "rst.payments", null, "configurations/rst.payments.conf.json")]
    public void GetPath_PutsEveryKindIntoItsFolder(ViewDocumentKind kind, string? type, string? key, ulong? version, string expected)
    {
        ViewArchiveNames.GetPath(kind, type, key, version).ShouldBe(expected);
    }

    [Theory]
    [InlineData(ViewArchiveContent.Annotations, "annotations")]
    [InlineData(ViewArchiveContent.Schemas, "schemas")]
    [InlineData(ViewArchiveContent.Templates, "templates")]
    [InlineData(ViewArchiveContent.Configurations, "configurations")]
    public void GetFolder_IsTheKindInLowerCase(ViewArchiveContent content, string expected)
    {
        ViewArchiveNames.GetFolder(content).ShouldBe(expected);
    }

    [Theory]
    [InlineData(ViewDocumentKind.Annotation, null, "rst.payments", "annotation rst.payments")]
    [InlineData(ViewDocumentKind.TypeSchema, "exe", null, "type schema exe")]
    [InlineData(ViewDocumentKind.DescendantTypeSchema, "exe", "sbt.northwind", "exe schema below sbt.northwind")]
    [InlineData(ViewDocumentKind.AnnotationSchema, null, "rst.payments", "schema of rst.payments")]
    [InlineData(ViewDocumentKind.Template, "exe", null, "template exe")]
    [InlineData(ViewDocumentKind.Configuration, null, "rst.payments", "configuration of rst.payments")]
    public void Describe_NamesTheDocument(ViewDocumentKind kind, string? type, string? key, string expected)
    {
        ViewArchiveNames.Describe(kind, type, key).ShouldBe(expected);
    }

    [Theory]
    [InlineData(ViewArchiveFormat.Folder, "export-acme-main-default-20261014-013012Z.zip")]
    [InlineData(ViewArchiveFormat.Git, "export-acme-main-default-git-20261014-013012Z.zip")]
    public void GetArchiveFileName_HasTheUtcTime(ViewArchiveFormat format, string expected)
    {
        var createdAt = new DateTimeOffset(2026, 10, 14, 3, 30, 12, TimeSpan.FromHours(2));
        ViewArchiveNames.GetArchiveFileName("acme", "main", "default", format, createdAt).ShouldBe(expected);
    }

    private static (ViewDocumentKind Kind, string? Type, string? Key, ulong? Version) Parse(string fileName)
    {
        ViewArchiveNames.TryParse(fileName, out var name).ShouldBeTrue(fileName);
        name.ErrorCode.ShouldBeNull();
        return (name.Kind, name.AnnotationType, name.AnnotationKey?.ToString(), name.Version);
    }
}
