using _42.Platform.Storyteller.Sdk.Configuring;
using _42.Platform.Storyteller.Sdk.Hosting.Reading;
using Newtonsoft.Json.Linq;

namespace _42.Platform.Storyteller.Sdk.Hosting.UnitTests.Catalog;

// The default implementations of IStorytellerCatalog's GetWithAncestorsAsync and ListWithAncestorsAsync, which a test
// double of an application gets: over a double that implements the four older members only.
public sealed class CatalogDefaultsTests
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

    private readonly DoubleCatalog _double = new();

    // The default members are members of the interface, not of the double.
    private IStorytellerCatalog Catalog => _double;

    [Fact]
    public async Task GetWithAncestorsAsync_ReadsTheKey_ThenEachAncestorByItsFullKey_WithGetAsync()
    {
        _double.PutWithAncestors(NorthwindProd);
        var before = DateTimeOffset.UtcNow;

        var context = (await Catalog.GetWithAncestorsAsync(Full(NorthwindProd))).ShouldNotBeNull();

        context.Key.ShouldBe(Full(NorthwindProd));
        context.Annotation.ShouldBeSameAs(_double.Find(NorthwindProd));
        context.Chain.Select(annotation => annotation.AnnotationKey).ShouldBe([NorthwindProd, NorthwindInProd, NorthwindUsage, Northwind, Responsibility]);
        context.Subject.ShouldBeSameAs(_double.Find(Northwind));
        context.Unreadable.ShouldBeEmpty();
        context.ReadAt.ShouldBeInRange(before, DateTimeOffset.UtcNow);
        _double.Gets.ShouldBe([Full(NorthwindProd), Full(Responsibility), Full(Northwind), Full(NorthwindUsage), Full(NorthwindInProd)]);
    }

    // The keys of the ancestors come from the full key of the snapshot, so a key that the double takes as an annotation
    // key works too.
    [Fact]
    public async Task GetWithAncestorsAsync_OfAnAnnotationKey_AsksForTheAncestorsByTheFullKeyOfTheSnapshot()
    {
        _double.PutWithAncestors(NorthwindUsage);

        var context = (await Catalog.GetWithAncestorsAsync(NorthwindUsage)).ShouldNotBeNull();

        context.Key.ShouldBe(Full(NorthwindUsage));
        context.Ancestors.Keys.ShouldBe([AnnotationType.Responsibility, AnnotationType.Subject], ignoreOrder: true);
        _double.Gets.ShouldBe([NorthwindUsage, Full(Responsibility), Full(Northwind)]);
    }

    [Fact]
    public async Task GetWithAncestorsAsync_OfAKeyThatGetAsyncDoesNotFind_IsNull_AndAsksForNoAncestor()
    {
        (await Catalog.GetWithAncestorsAsync(Full(NorthwindProd))).ShouldBeNull();

        _double.Gets.ShouldHaveSingleItem();
    }

    [Fact]
    public async Task GetWithAncestorsAsync_AnAncestorThatGetAsyncDoesNotFind_IsLeftOut()
    {
        _double.PutWithAncestors(NorthwindProd);
        _double.Remove(NorthwindInProd);

        var context = (await Catalog.GetWithAncestorsAsync(Full(NorthwindProd))).ShouldNotBeNull();

        context.Context.ShouldBeNull();
        context.Unreadable.ShouldBeEmpty();
        context.Ancestors.Count.ShouldBe(3);
    }

    // The defaults know no restrictions: GetAsync throws on a 403, so the call throws, and nothing lands in Unreadable.
    [Fact]
    public async Task GetWithAncestorsAsync_AnAncestorThatGetAsyncThrowsFor_Throws()
    {
        _double.PutWithAncestors(NorthwindProd);
        var restricted = new StorytellerConfigurationException("Restricted.", Full(Northwind), 403, "AnnotationRestricted", false);
        _double.Fail(Northwind, restricted);

        var exception = await Should.ThrowAsync<StorytellerConfigurationException>(() => Catalog.GetWithAncestorsAsync(Full(NorthwindProd)));

        exception.ShouldBeSameAs(restricted);
    }

    [Fact]
    public async Task GetWithAncestorsAsync_OfASnapshotWithoutAFullKey_Throws_NamingItsKey()
    {
        _double.Answer(NorthwindUsage, new StorytellerAnnotation { Key = NorthwindUsage, AnnotationKey = NorthwindUsage, Type = AnnotationType.Usage, Name = "invoicing" });

        var exception = await Should.ThrowAsync<InvalidOperationException>(() => Catalog.GetWithAncestorsAsync(NorthwindUsage));

        exception.Message.ShouldBe(
            "The catalog answered a snapshot whose Key, 'usg.northwind.invoicing', is not a full key, @{organization}.{project}.{view}.{annotation key}, so the keys of its ancestors are not known. Give the snapshots of a test double their full keys.");
        _double.Gets.ShouldHaveSingleItem();
    }

    // The double lists in the reverse order of the keys; the contexts are by key.
    [Fact]
    public async Task ListWithAncestorsAsync_BuildsOnListAsyncAndGetAsync_AndAsksForEachDistinctAncestorOnce()
    {
        PutExecutions();
        var target = StorytellerTarget.ExecutionsOf(Responsibility);
        var before = DateTimeOffset.UtcNow;

        var contexts = await Catalog.ListWithAncestorsAsync(target);

        contexts.Select(context => context.Key).ShouldBe([Full(ContosoProd), Full(NorthwindDev), Full(NorthwindProd)]);
        contexts.ShouldAllBe(context => context.Annotation is StorytellerExecution && context.Ancestors.Count == 4 && context.Unreadable.Count == 0);
        contexts[0].Annotation.ShouldBeSameAs(_double.Find(ContosoProd));
        contexts[1].Subject.ShouldBeSameAs(contexts[2].Subject);
        contexts[1].Chain.Select(annotation => annotation.AnnotationKey).ShouldBe([NorthwindDev, NorthwindInDev, NorthwindUsage, Northwind, Responsibility]);
        contexts.Select(context => context.ReadAt).Distinct().ShouldHaveSingleItem().ShouldBeInRange(before, DateTimeOffset.UtcNow);
        _double.Listed.ShouldHaveSingleItem().ShouldBeSameAs(target);
        _double.Gets.ShouldBe(
            new[] { Responsibility, Northwind, Contoso, NorthwindUsage, ContosoUsage, NorthwindInProd, NorthwindInDev, ContosoInProd }.Select(Full),
            ignoreOrder: true);
    }

    // The double's ListAsync knows nothing of the requirement, so the default keeps the descendants itself. Without the
    // requirement every descendant is listed, with the status of its chain.
    [Fact]
    public async Task ListWithAncestorsAsync_OfATargetThatRequiresActiveAncestors_KeepsTheDescendantsUnderThem()
    {
        PutExecutions();
        _double.PutWithAncestors("exe.contoso.invoicing.dev");
        _double.Put(Northwind, json => json["IsDisabled"] = true);
        _double.Remove("cnt.contoso.dev");
        var target = StorytellerTarget.ExecutionsOf(Responsibility);

        var required = await Catalog.ListWithAncestorsAsync(target.RequiringActiveAncestors());
        var all = await Catalog.ListWithAncestorsAsync(target);

        required.ShouldHaveSingleItem().Annotation.AnnotationKey.ShouldBe(ContosoProd);
        all.Select(context => (context.Annotation.AnnotationKey, context.GetSubscriptionStatus(DateTimeOffset.UtcNow, StorytellerStatusScope.Chain))).ShouldBe(
        [
            ("exe.contoso.invoicing.dev", StorytellerSubscriptionStatus.Active),
            (ContosoProd, StorytellerSubscriptionStatus.Active),
            (NorthwindDev, StorytellerSubscriptionStatus.Disabled),
            (NorthwindProd, StorytellerSubscriptionStatus.Disabled),
        ]);
        all[0].Context.ShouldBeNull();
        all.ShouldAllBe(context => context.Unreadable.Count == 0);
    }

    [Fact]
    public async Task ListWithAncestorsAsync_AnAncestorThatGetAsyncThrowsFor_Throws()
    {
        PutExecutions();
        var restricted = new StorytellerConfigurationException("Restricted.", Full(Contoso), 403, "AnnotationRestricted", false);
        _double.Fail(Contoso, restricted);

        var exception = await Should.ThrowAsync<StorytellerConfigurationException>(() => Catalog.ListWithAncestorsAsync(StorytellerTarget.ExecutionsOf(Responsibility)));

        exception.ShouldBeSameAs(restricted);
    }

    // A list has no key argument: the keys of the ancestors come from the Key of each descendant, as for one key.
    [Fact]
    public async Task ListWithAncestorsAsync_OfADescendantWithoutAFullKey_Throws_NamingItsKey()
    {
        _double.Answer(NorthwindProd, new StorytellerExecution { Key = NorthwindProd, AnnotationKey = NorthwindProd, Type = AnnotationType.Execution, Name = "prod", SubjectName = "northwind", ResponsibilityName = "invoicing", ContextName = "prod" });

        var exception = await Should.ThrowAsync<InvalidOperationException>(() => Catalog.ListWithAncestorsAsync(StorytellerTarget.ExecutionsOf(Responsibility)));

        exception.Message.ShouldStartWith("The catalog answered a snapshot whose Key, 'exe.northwind.invoicing.prod', is not a full key");
        _double.Gets.ShouldBeEmpty();
    }

    [Fact]
    public async Task ListWithAncestorsAsync_OfNoDescendants_IsEmpty_AndAsksForNoAncestor()
    {
        _double.Put(Responsibility);

        var contexts = await Catalog.ListWithAncestorsAsync(StorytellerTarget.ExecutionsOf(Responsibility));

        contexts.ShouldBeEmpty();
        _double.Gets.ShouldBeEmpty();
    }

    [Fact]
    public async Task ListWithAncestorsAsync_WithoutATarget_IsRefused_BeforeAnyCall()
    {
        var exception = await Should.ThrowAsync<ArgumentNullException>(() => Catalog.ListWithAncestorsAsync<StorytellerExecution>(null!));

        exception.ParamName.ShouldBe("target");
        _double.Listed.ShouldBeEmpty();
    }

    private static string Full(string annotationKey)
    {
        return $"{HostingFixture.Scope}.{annotationKey}";
    }

    // Three executions of the responsibility, of two subjects in three contexts, with every ancestor of theirs, all
    // active.
    private void PutExecutions()
    {
        _double.PutWithAncestors(NorthwindProd);
        _double.PutWithAncestors(NorthwindDev);
        _double.PutWithAncestors(ContosoProd);
    }

    // A test double as an application writes one: the four members that the interface had before, over snapshots by
    // full key. It takes an annotation key in its own scope, lists every snapshot of the type in the reverse order of
    // the keys, and knows nothing of RequireActiveAncestors.
    private sealed class DoubleCatalog : IStorytellerCatalog
    {
        private readonly Dictionary<string, StorytellerAnnotation> _annotations = new(StringComparer.Ordinal);
        private readonly Dictionary<string, Exception> _failures = new(StringComparer.Ordinal);

        // The keys that GetAsync was asked for, as given.
        public List<string> Gets { get; } = [];

        // The targets that ListAsync was asked for.
        public List<object> Listed { get; } = [];

        // The typed snapshot of the key, as a point read gives it.
        public void Put(string annotationKey, Action<JObject>? configure = null)
        {
            var key = FullKey.Parse(Full(annotationKey));
            var annotation = JObject.Parse(TestAnnotations.Json(annotationKey, configure)).ToObject<Annotation>()!;
            _annotations[key.ToString()] = AnnotationMapper.Map(key, annotation);
        }

        // The annotation of the key, and every ancestor of it that the double does not hold yet, all active.
        public void PutWithAncestors(string annotationKey)
        {
            foreach (var ancestor in AnnotationKey.Parse(annotationKey).GetAllAncestorKeys().Select(ancestor => ancestor.ToString()))
            {
                if (!_annotations.ContainsKey(Full(ancestor)))
                {
                    Put(ancestor);
                }
            }

            Put(annotationKey);
        }

        // What GetAsync answers for the key, whatever the snapshot's own key is.
        public void Answer(string annotationKey, StorytellerAnnotation annotation)
        {
            _annotations[Full(annotationKey)] = annotation;
        }

        public void Remove(string annotationKey)
        {
            _annotations.Remove(Full(annotationKey));
        }

        // GetAsync of the key throws.
        public void Fail(string annotationKey, Exception failure)
        {
            _failures[Full(annotationKey)] = failure;
        }

        public StorytellerAnnotation? Find(string annotationKey)
        {
            return _annotations.GetValueOrDefault(Full(annotationKey));
        }

        public Task<StorytellerAnnotation?> GetAsync(string key, CancellationToken cancellationToken = default)
        {
            Gets.Add(key);
            var fullKey = key.StartsWith('@') ? key : Full(key);

            return _failures.TryGetValue(fullKey, out var failure)
                ? Task.FromException<StorytellerAnnotation?>(failure)
                : Task.FromResult(_annotations.GetValueOrDefault(fullKey));
        }

        public async Task<TAnnotation?> GetAsync<TAnnotation>(string key, CancellationToken cancellationToken = default)
            where TAnnotation : StorytellerAnnotation
        {
            return await GetAsync(key, cancellationToken) as TAnnotation;
        }

        public Task<IReadOnlyList<TAnnotation>> ListAsync<TAnnotation>(StorytellerTarget<TAnnotation> target, CancellationToken cancellationToken = default)
            where TAnnotation : StorytellerAnnotation
        {
            Listed.Add(target);
            IReadOnlyList<TAnnotation> descendants = _annotations.Values
                .OfType<TAnnotation>()
                .OrderByDescending(annotation => annotation.Key, StringComparer.Ordinal)
                .ToList();

            return Task.FromResult(descendants);
        }

        public IAsyncEnumerable<StorytellerAnnotationChange<TAnnotation>> WatchAsync<TAnnotation>(StorytellerTarget<TAnnotation> target, TimeSpan? pollInterval = null, CancellationToken cancellationToken = default)
            where TAnnotation : StorytellerAnnotation => throw new NotSupportedException();
    }
}
