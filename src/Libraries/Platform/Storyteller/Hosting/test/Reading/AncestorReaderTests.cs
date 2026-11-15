using _42.Platform.Storyteller.Sdk.Configuring;
using _42.Platform.Storyteller.Sdk.Hosting.Reading;
using Newtonsoft.Json.Linq;

namespace _42.Platform.Storyteller.Sdk.Hosting.UnitTests.Reading;

// AncestorReader.ReadDistinctAsync: the distinct ancestors of several keys, which the catalog's lists and the descendant
// informer both read through it, each with its own read of one ancestor.
public sealed class AncestorReaderTests
{
    private const string Responsibility = "rst.invoicing";
    private const string Northwind = "sbt.northwind";
    private const string Contoso = "sbt.contoso";
    private const string NorthwindUsage = "usg.northwind.invoicing";
    private const string ContosoUsage = "usg.contoso.invoicing";
    private const string NorthwindInProd = "cnt.northwind.prod";
    private const string NorthwindInDev = "cnt.northwind.dev";
    private const string ContosoInProd = "cnt.contoso.prod";
    private const string NorthwindProd = "exe.northwind.invoicing.prod";
    private const string NorthwindDev = "exe.northwind.invoicing.dev";
    private const string ContosoProd = "exe.contoso.invoicing.prod";

    // Three executions of the responsibility, of two subjects in three contexts.
    private static readonly FullKey[] Executions = [Key(NorthwindProd), Key(NorthwindDev), Key(ContosoProd)];

    // Their distinct ancestors, but the responsibility.
    private static readonly string[] OtherAncestors = [Northwind, Contoso, NorthwindUsage, ContosoUsage, NorthwindInProd, NorthwindInDev, ContosoInProd];

    // The full keys that the read was asked for.
    private readonly List<string> _asked = [];

    [Fact]
    public async Task ReadDistinctAsync_ReadsEachDistinctAncestorOnce_ByFullKey()
    {
        var reads = await AncestorReader.ReadDistinctAsync(Executions, known: null, ReadAsync);

        _asked.Count.ShouldBe(8);
        _asked.ShouldBe(OtherAncestors.Append(Responsibility).Select(Full), ignoreOrder: true);
        reads.Keys.ShouldBe(_asked, ignoreOrder: true);
        reads[Full(Northwind)].Annotation.ShouldNotBeNull().AnnotationKey.ShouldBe(Northwind);
        reads.Values.ShouldAllBe(read => read.Failure == null);
    }

    // The target's own ancestor, which a list and the first poll of a watch read before: it is not read again.
    [Fact]
    public async Task ReadDistinctAsync_AnAncestorThatIsKnown_ServesWithoutASecondRead()
    {
        var known = Snapshot(Key(Responsibility));

        var reads = await AncestorReader.ReadDistinctAsync(Executions, known, ReadAsync);

        reads[Full(Responsibility)].ShouldBeSameAs(known);
        _asked.ShouldBe(OtherAncestors.Select(Full), ignoreOrder: true);
        reads.Count.ShouldBe(8);
    }

    [Fact]
    public async Task ReadDistinctAsync_AKnownOneThatIsNoAncestorOfTheKeys_IsLeftOut()
    {
        var reads = await AncestorReader.ReadDistinctAsync([Key(NorthwindUsage)], Snapshot(Key(Contoso)), ReadAsync);

        reads.Keys.ShouldBe([Full(Responsibility), Full(Northwind)], ignoreOrder: true);
        _asked.Count.ShouldBe(2);
    }

    // The informer's read keeps a failure as an answer: it is in the reads, and no other read is lost for it.
    [Fact]
    public async Task ReadDistinctAsync_KeepsWhatTheReadAnswers_AFailureIncluded()
    {
        var failure = new StorytellerConfigurationException("The subject did not come.", Full(Northwind), 503, null, true);

        var reads = await AncestorReader.ReadDistinctAsync(
            Executions,
            known: null,
            key => key.Annotation.ToString() == Northwind ? Task.FromResult(new AncestorRead(key, null, failure)) : ReadAsync(key));

        reads.Count.ShouldBe(8);
        reads[Full(Northwind)].Failure.ShouldBeSameAs(failure);
        reads[Full(Northwind)].IsUndecided.ShouldBeTrue();
        reads[Full(Contoso)].Annotation.ShouldNotBeNull();
    }

    // The lists' read throws any failure but a 403 AnnotationRestricted and a 404. Every read ends before it is thrown.
    [Fact]
    public async Task ReadDistinctAsync_AReadThatThrows_Throws_AfterEveryReadEnded()
    {
        var failure = new StorytellerConfigurationException("The subject did not come.", Full(Northwind), 503, null, true);
        var held = new TaskCompletionSource<AncestorRead>(TaskCreationOptions.RunContinuationsAsynchronously);

        var reading = AncestorReader.ReadDistinctAsync(Executions, known: null, key =>
        {
            var read = ReadAsync(key);

            return key.Annotation.ToString() switch
            {
                Northwind => Task.FromException<AncestorRead>(failure),
                Contoso => held.Task,
                _ => read,
            };
        });

        _asked.Count.ShouldBe(8);
        reading.IsCompleted.ShouldBeFalse();

        held.SetResult(Snapshot(Key(Contoso)));

        (await Should.ThrowAsync<StorytellerConfigurationException>(() => reading)).ShouldBeSameAs(failure);
    }

    [Fact]
    public async Task ReadDistinctAsync_OfNoKeys_ReadsNothing()
    {
        var reads = await AncestorReader.ReadDistinctAsync([], Snapshot(Key(Responsibility)), ReadAsync);

        reads.ShouldBeEmpty();
        _asked.ShouldBeEmpty();
    }

    private static string Full(string annotationKey)
    {
        return $"{HostingFixture.Scope}.{annotationKey}";
    }

    private static FullKey Key(string annotationKey)
    {
        return FullKey.Parse(Full(annotationKey));
    }

    // The read of an ancestor that is there, active.
    private static AncestorRead Snapshot(FullKey key)
    {
        var annotation = JObject.Parse(TestAnnotations.Json(key.Annotation.ToString())).ToObject<Annotation>()!;
        return new AncestorRead(key, AnnotationMapper.Map(key, annotation), null);
    }

    private Task<AncestorRead> ReadAsync(FullKey key)
    {
        _asked.Add(key.ToString());
        return Task.FromResult(Snapshot(key));
    }
}
