using _42.Platform.Storyteller.Sdk.Configuring.Reading;

namespace _42.Platform.Storyteller.Sdk.Configuring.UnitTests.Reading;

public class DocumentIdentityTests
{
    [Fact]
    public void TheParts_OfAFullKey()
    {
        var identity = Create("https://storyteller.example.com/api/", "@acme.billing.default.SBT.Northwind");

        identity.Endpoint.ShouldBe("https://storyteller.example.com/api");
        identity.Organization.ShouldBe("acme");
        identity.Project.ShouldBe("billing");
        identity.View.ShouldBe("default");

        // The type code is lower-cased; the names keep their casing.
        identity.AnnotationKey.ShouldBe("sbt.Northwind");
        identity.Key.ShouldBe("@acme.billing.default.sbt.Northwind");
        identity.Mode.ShouldBe(StorytellerReadMode.Resolved);
    }

    [Fact]
    public void TheCaseOfTheTypeCode_AndATrailingSlash_DoNotMakeAnotherIdentity()
    {
        Create("https://storyteller.example.com/api/", "@acme.billing.default.UXE.a.b.c.d")
            .ShouldBe(Create("https://storyteller.example.com/api", "@acme.billing.default.uxe.a.b.c.d"));
    }

    [Theory]
    [InlineData("https://other.example.com/api", TestDocuments.Key, StorytellerReadMode.Resolved)]
    [InlineData(TestDocuments.Endpoint, "@acme.billing.prod.sbt.northwind", StorytellerReadMode.Resolved)]
    [InlineData(TestDocuments.Endpoint, "@acme.billing.default.sbt.Northwind", StorytellerReadMode.Resolved)]
    [InlineData(TestDocuments.Endpoint, TestDocuments.Key, StorytellerReadMode.Effective)]
    public void EveryPart_Counts(string endpoint, string key, StorytellerReadMode mode)
    {
        var identity = Create(endpoint, key, mode);
        var reference = Create(TestDocuments.Endpoint, TestDocuments.Key);

        identity.ShouldNotBe(reference);
        identity.ToCanonicalString().ShouldNotBe(reference.ToCanonicalString());
    }

    [Fact]
    public void TheCanonicalText_IsAJsonArray_AndHasNoUserInfo()
    {
        var identity = Create("https://user:p4ss@storyteller.example.com/api", TestDocuments.Key, StorytellerReadMode.Effective);

        identity.ToCanonicalString().ShouldBe("""["https://storyteller.example.com/api","acme","billing","default","sbt.northwind","effective"]""");
    }

    private static DocumentIdentity Create(string endpoint, string key, StorytellerReadMode mode = StorytellerReadMode.Resolved)
    {
        return DocumentIdentity.Create(new Uri(endpoint), FullKey.Parse(key), mode);
    }
}
