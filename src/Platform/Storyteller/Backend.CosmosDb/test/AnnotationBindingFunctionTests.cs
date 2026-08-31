#nullable enable

using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using _42.Platform.Storyteller.Annotating;
using _42.Platform.Storyteller.Binding;
using _42.Platform.Storyteller.Binding.Language;
using _42.Platform.Storyteller.Configuring;
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
        var function = new AnnotationBindingFunction(service);

        var result = await function.InvokeAsync(CreateRequest(ExecutionKey, "/title"));

        result!.Token.Value<string>().Should().Be("hello");
    }

    [Fact]
    public async Task InvokeAsync_TwoArguments_ResolvesAgainstAncestorOfGivenType()
    {
        var service = new FakeAnnotationService();
        var responsibilityKey = FullKey.Create(AnnotationKey.CreateResponsibility("myresp"), ExecutionKey);
        service.Set(responsibilityKey, new Dictionary<string, object> { ["owner"] = "team-a" });
        var function = new AnnotationBindingFunction(service);

        var result = await function.InvokeAsync(CreateRequest(ExecutionKey, "/owner", "Responsibility"));

        result!.Token.Value<string>().Should().Be("team-a");
    }

    [Fact]
    public async Task InvokeAsync_TwoArguments_TypeNameIsCaseInsensitive()
    {
        var service = new FakeAnnotationService();
        var subjectKey = FullKey.Create(AnnotationKey.CreateSubject("mysubject"), ExecutionKey);
        service.Set(subjectKey, new Dictionary<string, object> { ["owner"] = "team-b" });
        var function = new AnnotationBindingFunction(service);

        var result = await function.InvokeAsync(CreateRequest(ExecutionKey, "/owner", "subject"));

        result!.Token.Value<string>().Should().Be("team-b");
    }

    [Fact]
    public async Task InvokeAsync_MissingAnnotation_ReturnsNull()
    {
        var service = new FakeAnnotationService();
        var function = new AnnotationBindingFunction(service);

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
        var function = new AnnotationBindingFunction(service);

        var result = await function.InvokeAsync(CreateRequest(ExecutionKey, "/title"));

        result.Should().BeNull();
    }

    [Fact]
    public async Task InvokeAsync_InvalidAnnotationTypeName_Throws()
    {
        var service = new FakeAnnotationService();
        var function = new AnnotationBindingFunction(service);

        var act = () => function.InvokeAsync(CreateRequest(ExecutionKey, "/title", "NotAType")).AsTask();

        await act.Should().ThrowAsync<BindingEvaluationException>();
    }

    [Fact]
    public async Task InvokeAsync_TypeNotAnAncestor_Throws()
    {
        var service = new FakeAnnotationService();
        var function = new AnnotationBindingFunction(service);
        var responsibilityKey = FullKey.Create(AnnotationKey.CreateResponsibility("myresp"), ExecutionKey);

        var act = () => function.InvokeAsync(CreateRequest(responsibilityKey, "/title", "Subject")).AsTask();

        await act.Should().ThrowAsync<BindingEvaluationException>();
    }

    [Fact]
    public async Task InvokeAsync_MissingContext_Throws()
    {
        var service = new FakeAnnotationService();
        var function = new AnnotationBindingFunction(service);
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
        var function = new AnnotationBindingFunction(service);
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

    private static BindingFunctionRequest CreateRequest(FullKey key, string expression, string? annotationType = null)
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
        };
    }

    private sealed class FakeAnnotationService : IAnnotationService
    {
        private readonly Dictionary<string, Annotation> _annotations = new();

        public void Set(FullKey key, IReadOnlyDictionary<string, object> values)
        {
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

        public Task<AnnotationsResponse> GetAnnotationsAsync(AnnotationsRequest request) => throw new NotSupportedException();

        public Task<IEnumerable<Annotation>> CreateAnnotationAsync(string organization, Annotation annotation) => throw new NotSupportedException();

        public Task UpdateAnnotationAsync(string organization, Annotation annotation) => throw new NotSupportedException();

        public Task<IEnumerable<Annotation>> CreateAnnotationsAsync(string organization, IEnumerable<Annotation> annotations) => throw new NotSupportedException();

        public Task<IEnumerable<Annotation>> CreateAnnotationsFromStringAsync(string organization, IEnumerable<string> annotations) => throw new NotSupportedException();

        public Task DeleteAnnotationAsync(FullKey fullKey) => throw new NotSupportedException();
    }
}
