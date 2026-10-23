#nullable enable

using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using _42.Platform.Storyteller.Annotating;
using _42.Platform.Storyteller.Binding;
using _42.Platform.Storyteller.Binding.Language;
using _42.Platform.Storyteller.Configuring;
using _42.Platform.Storyteller.Transferring;
using FluentAssertions;
using Newtonsoft.Json.Linq;
using Xunit;

namespace _42.Platform.Storyteller.Backend.CosmosDb.UnitTests;

public class AnnotationBindingFunctionTests
{
    private static readonly FullKey ExecutionKey = FullKey.Create(
        AnnotationKey.CreateExecution("mysubject", "myresp", "myctx"), "org", "proj", "view");

    [Fact]
    public async Task InvokeAsync_OneArgument_ResolvesAgainstDirectlyLinkedAnnotation()
    {
        var service = new FakeAnnotationService();
        service.Set(ExecutionKey, new Dictionary<string, object> { ["title"] = "hello" });
        var function = new AnnotationBindingFunction(new Lazy<IAnnotationService>(() => service));

        var result = await function.InvokeAsync(CreateRequest(ExecutionKey, "/title"));

        result!.Token.Value<string>().Should().Be("hello");
    }

    [Fact]
    public async Task InvokeAsync_TwoArguments_ResolvesAgainstAncestorOfGivenType()
    {
        var service = new FakeAnnotationService();
        var responsibilityKey = FullKey.Create(AnnotationKey.CreateResponsibility("myresp"), ExecutionKey);
        service.Set(responsibilityKey, new Dictionary<string, object> { ["owner"] = "team-a" });
        var function = new AnnotationBindingFunction(new Lazy<IAnnotationService>(() => service));

        var result = await function.InvokeAsync(CreateRequest(ExecutionKey, "/owner", "Responsibility"));

        result!.Token.Value<string>().Should().Be("team-a");
    }

    [Fact]
    public async Task InvokeAsync_TwoArguments_TypeNameIsCaseInsensitive()
    {
        var service = new FakeAnnotationService();
        var subjectKey = FullKey.Create(AnnotationKey.CreateSubject("mysubject"), ExecutionKey);
        service.Set(subjectKey, new Dictionary<string, object> { ["owner"] = "team-b" });
        var function = new AnnotationBindingFunction(new Lazy<IAnnotationService>(() => service));

        var result = await function.InvokeAsync(CreateRequest(ExecutionKey, "/owner", "subject"));

        result!.Token.Value<string>().Should().Be("team-b");
    }

    [Fact]
    public async Task InvokeAsync_MissingAnnotation_ReturnsNull()
    {
        var service = new FakeAnnotationService();
        var function = new AnnotationBindingFunction(new Lazy<IAnnotationService>(() => service));

        var result = await function.InvokeAsync(CreateRequest(ExecutionKey, "/title"));

        result.Should().BeNull();
    }

    [Fact]
    public async Task InvokeAsync_AnnotationWithNullValues_ReturnsNull()
    {
        var service = new FakeAnnotationService();
        service.SetAnnotation(ExecutionKey, new Annotation
        {
            ProjectName = "proj",
            ViewName = "view",
            AnnotationKey = ExecutionKey.Annotation,
            Name = "myctx",
            AnnotationType = AnnotationType.Execution,
            Values = null,
        });
        var function = new AnnotationBindingFunction(new Lazy<IAnnotationService>(() => service));

        var result = await function.InvokeAsync(CreateRequest(ExecutionKey, "/title"));

        result.Should().BeNull();
    }

    [Fact]
    public async Task InvokeAsync_InvalidAnnotationTypeName_Throws()
    {
        var service = new FakeAnnotationService();
        var function = new AnnotationBindingFunction(new Lazy<IAnnotationService>(() => service));

        var act = () => function.InvokeAsync(CreateRequest(ExecutionKey, "/title", "NotAType")).AsTask();

        await act.Should().ThrowAsync<BindingEvaluationException>();
    }

    [Fact]
    public async Task InvokeAsync_TypeNotAnAncestor_Throws()
    {
        var service = new FakeAnnotationService();
        var function = new AnnotationBindingFunction(new Lazy<IAnnotationService>(() => service));
        var responsibilityKey = FullKey.Create(AnnotationKey.CreateResponsibility("myresp"), ExecutionKey);

        var act = () => function.InvokeAsync(CreateRequest(responsibilityKey, "/title", "Subject")).AsTask();

        await act.Should().ThrowAsync<BindingEvaluationException>();
    }

    [Fact]
    public async Task InvokeAsync_RecordsTheMarkerOfTheAnnotation()
    {
        var service = new FakeAnnotationService();
        service.Set(ExecutionKey, new Dictionary<string, object> { ["title"] = "hello" }, marker: "\"7\"");
        var function = new AnnotationBindingFunction(new Lazy<IAnnotationService>(() => service));
        var dependencies = Recording();

        await function.InvokeAsync(CreateRequest(ExecutionKey, "/title", dependencies: dependencies));

        dependencies.IsVolatile.Should().BeFalse();
        dependencies.ToList().Should().ContainSingle().Which.Should().Be(
            new BindingDependency(BindingDependencyKinds.Annotation, "view", ExecutionKey.Annotation.ToString(), "\"7\""));
    }

    [Fact]
    public async Task InvokeAsync_MissingAnnotation_RecordsAbsent()
    {
        var service = new FakeAnnotationService();
        var function = new AnnotationBindingFunction(new Lazy<IAnnotationService>(() => service));
        var dependencies = Recording();

        var result = await function.InvokeAsync(CreateRequest(ExecutionKey, "/title", dependencies: dependencies));

        result.Should().BeNull();
        dependencies.ToList().Should().ContainSingle().Which.Marker.Should().Be(BindingDependency.Absent);
    }

    [Fact]
    public async Task InvokeAsync_Ancestor_RecordsItsOwnKey()
    {
        var service = new FakeAnnotationService();
        var subjectKey = FullKey.Create(AnnotationKey.CreateSubject("mysubject"), ExecutionKey);
        service.Set(subjectKey, new Dictionary<string, object> { ["owner"] = "team-b" }, marker: "\"3\"");
        var function = new AnnotationBindingFunction(new Lazy<IAnnotationService>(() => service));
        var dependencies = Recording();

        await function.InvokeAsync(CreateRequest(ExecutionKey, "/owner", "Subject", dependencies));

        dependencies.ToList().Should().ContainSingle().Which.Should().Be(
            new BindingDependency(BindingDependencyKinds.Annotation, "view", subjectKey.Annotation.ToString(), "\"3\""));
    }

    [Fact]
    public async Task InvokeAsync_AServiceWithoutMarkers_MakesItVolatile()
    {
        var service = new FakeAnnotationService { GivesMarkers = false };
        service.Set(ExecutionKey, new Dictionary<string, object> { ["title"] = "hello" });
        var function = new AnnotationBindingFunction(new Lazy<IAnnotationService>(() => service));
        var dependencies = Recording();

        var result = await function.InvokeAsync(CreateRequest(ExecutionKey, "/title", dependencies: dependencies));

        result!.Token.Value<string>().Should().Be("hello");
        dependencies.IsVolatile.Should().BeTrue();
    }

    [Fact]
    public void Function_Records()
    {
        new AnnotationBindingFunction(new Lazy<IAnnotationService>(() => new FakeAnnotationService())).Should().BeAssignableTo<IRecordingBinding>();
    }

    [Fact]
    public async Task InvokeAsync_MissingContext_Throws()
    {
        var service = new FakeAnnotationService();
        var function = new AnnotationBindingFunction(new Lazy<IAnnotationService>(() => service));
        var request = new BindingFunctionRequest
        {
            Name = "annotation",
            Arguments = new List<BindingValue> { BindingValue.FromString("/title") },
            IncludeSecrets = true,
            Context = null,
        };

        var act = () => function.InvokeAsync(request).AsTask();

        await act.Should().ThrowAsync<BindingEvaluationException>();
    }

    [Fact]
    public async Task InvokeAsync_WrongArgumentCount_Throws()
    {
        var service = new FakeAnnotationService();
        var function = new AnnotationBindingFunction(new Lazy<IAnnotationService>(() => service));
        var request = new BindingFunctionRequest
        {
            Name = "annotation",
            Arguments = new List<BindingValue>
            {
                BindingValue.FromString("/title"),
                BindingValue.FromString("Subject"),
                BindingValue.FromString("extra"),
            },
            IncludeSecrets = true,
            Context = new ConfigurationBindingContext(ExecutionKey),
        };

        var act = () => function.InvokeAsync(request).AsTask();

        await act.Should().ThrowAsync<BindingEvaluationException>();
    }

    private static BindingDependencies Recording()
    {
        return new BindingScope { Dependencies = new BindingDependencies() }.Dependencies!;
    }

    private static BindingFunctionRequest CreateRequest(FullKey key, string expression, string? annotationType = null, BindingDependencies? dependencies = null)
    {
        var arguments = new List<BindingValue> { BindingValue.FromString(expression) };
        if (annotationType is not null)
        {
            arguments.Add(BindingValue.FromString(annotationType));
        }

        return new BindingFunctionRequest
        {
            Name = "annotation",
            Arguments = arguments,
            IncludeSecrets = true,
            Context = new ConfigurationBindingContext(key),
            Dependencies = dependencies,
        };
    }

    private sealed class FakeAnnotationService : IAnnotationService
    {
        private readonly Dictionary<string, Annotation> _annotations = new();
        private readonly Dictionary<string, string> _markers = new();

        // false: the default member of the interface, which cannot tell the marker
        public bool GivesMarkers { get; init; } = true;

        public void Set(FullKey key, IReadOnlyDictionary<string, object> values, string marker = "\"1\"")
        {
            _markers[key.ToString()] = marker;
            SetAnnotation(key, new Annotation
            {
                ProjectName = key.ProjectName,
                ViewName = key.ViewName,
                AnnotationKey = key.Annotation.ToString(),
                Name = key.Annotation.Name,
                AnnotationType = key.Annotation.Type,
                Values = values,
            });
        }

        public void SetAnnotation(FullKey key, Annotation annotation)
        {
            _annotations[key.ToString()] = annotation;
        }

        public Task<bool> ExistAnnotationAsync(FullKey fullKey) => throw new NotSupportedException();

        public Task<Annotation?> GetAnnotationAsync(FullKey fullKey)
        {
            _annotations.TryGetValue(fullKey.ToString(), out var annotation);
            return Task.FromResult(annotation);
        }

        public async Task<MarkedRead<Annotation>> GetAnnotationWithMarkerAsync(FullKey fullKey)
        {
            var annotation = await GetAnnotationAsync(fullKey);
            var marker = _markers.TryGetValue(fullKey.ToString(), out var stored) ? stored : BindingDependency.Absent;
            return new MarkedRead<Annotation>(annotation, GivesMarkers ? marker : null);
        }

        public Task<AnnotationsResponse> GetAnnotationsAsync(AnnotationsRequest request) => throw new NotSupportedException();

        public Task<IReadOnlyList<Annotation>> GetViewAnnotationsAsync(string organization, string project, string view, CancellationToken cancellationToken = default) => throw new NotSupportedException();

        public Task<Annotation?> GetDeletedAnnotationAsync(FullKey fullKey) => throw new NotSupportedException();

        public Task<AnnotationsResponse> GetDeletedAnnotationsAsync(string organization, string project, string view, bool rootsOnly = true, string? deletionId = null, string? continuationToken = null) => throw new NotSupportedException();

        public Task<IReadOnlyList<Annotation>> FindDeletedAnnotationsAsync(string organization, string project, string view, IReadOnlyCollection<AnnotationKey> keys) => throw new NotSupportedException();

        public Task<Annotation> SetAnnotationAsync(string organization, Annotation annotation, AuditActor actor) => throw new NotSupportedException();

        public Task<IEnumerable<Annotation>> CreateAnnotationAsync(string organization, Annotation annotation, AuditActor actor) => throw new NotSupportedException();

        public Task UpdateAnnotationAsync(string organization, Annotation annotation, AuditActor actor) => throw new NotSupportedException();

        public Task<IEnumerable<Annotation>> CreateAnnotationsAsync(string organization, IEnumerable<Annotation> annotations, AuditActor actor) => throw new NotSupportedException();

        public Task<IEnumerable<Annotation>> CreateAnnotationsFromStringAsync(string organization, string project, string view, IEnumerable<string> annotations, AuditActor actor) => throw new NotSupportedException();

        public Task<AnnotationDeletionResult> DeleteAnnotationAsync(FullKey fullKey, AuditActor actor) => throw new NotSupportedException();

        public Task<AnnotationRestoreResult> RestoreAnnotationsAsync(string organization, string project, string view, IReadOnlyCollection<AnnotationKey> keys, AuditActor actor, bool includeDescendants = true) => throw new NotSupportedException();

        public Task<AnnotationPurgeResult> PurgeAnnotationAsync(FullKey fullKey, AuditActor actor) => throw new NotSupportedException();

        public Task<AnnotationHistory?> GetAnnotationHistoryAsync(FullKey fullKey) => throw new NotSupportedException();

        public Task<IReadOnlySet<string>> FindActiveAnnotationKeysAsync(string organization, string project, string view, IReadOnlyCollection<AnnotationKey> keys, CancellationToken cancellationToken = default) => throw new NotSupportedException();

        public Task<ViewImportResult> ImportAnnotationAsync(FullKey key, JObject content, ViewImportOptions options, ViewImportOverlay overlay) => throw new NotSupportedException();
    }
}
