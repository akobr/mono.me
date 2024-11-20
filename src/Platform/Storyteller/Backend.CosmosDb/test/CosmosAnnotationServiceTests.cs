using System.Threading.Tasks;
using _42.Platform.Storyteller.Annotating;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace _42.Platform.Storyteller.Backend.CosmosDb.UnitTests;

public class CosmosAnnotationServiceTests(Startup startup)
    : BaseTestsClass(startup)
{
    [Fact]
    public async Task NonExistingAnnotation()
    {
        var annotations = Context.Services.GetRequiredService<IAnnotationService>();

        var annotationKey = AnnotationKey.CreateResponsibility("non-exist");
        var key = FullKey.Create(annotationKey, TestConstants.Organization, Constants.DefaultProjectName, Constants.DefaultViewName);
        var annotation = await annotations.GetAnnotationAsync(key);

        annotation.Should().BeNull();
    }

    [Fact]
    public async Task SubjectAnnotation()
    {
        var annotations = Context.Services.GetRequiredService<IAnnotationService>();

        var annotationKey = AnnotationKey.CreateSubject("simple-subject");
        var key = FullKey.Create(annotationKey, TestConstants.Organization, Constants.DefaultProjectName, Constants.DefaultViewName);
        await annotations.CreateAnnotationAsync(
            TestConstants.Organization,
            new Subject
            {
                AnnotationKey = annotationKey,
                AnnotationType = AnnotationType.Subject,
                Name = "simple-subject",
                ProjectName = Constants.DefaultProjectName,
                ViewName = Constants.DefaultViewName,
            });

        var returned = await annotations.GetAnnotationAsync(key);
        returned.Should().BeOfType<Subject>();
        var returnedTyped = (Subject)returned;
        returnedTyped.Name.Should().Be("simple-subject");
        returnedTyped.Contexts.Should().BeEmpty();
        returnedTyped.Usages.Should().BeEmpty();
    }

    [Fact]
    public async Task ResponsibilityAnnotation()
    {
        var annotations = Context.Services.GetRequiredService<IAnnotationService>();

        var annotationKey = AnnotationKey.CreateResponsibility("simple-responsibility");
        var key = FullKey.Create(annotationKey, TestConstants.Organization, Constants.DefaultProjectName, Constants.DefaultViewName);
        await annotations.CreateAnnotationAsync(
            TestConstants.Organization,
            new Responsibility
            {
                AnnotationKey = annotationKey,
                AnnotationType = AnnotationType.Responsibility,
                Name = "simple-responsibility",
                ProjectName = Constants.DefaultProjectName,
                ViewName = Constants.DefaultViewName,
            });

        var returned = await annotations.GetAnnotationAsync(key);
        returned.Should().BeOfType<Responsibility>();
        var returnedTyped = (Responsibility)returned;
        returnedTyped.Name.Should().Be("simple-responsibility");
    }

    [Fact]
    public async Task ContextAnnotation()
    {
        var annotations = Context.Services.GetRequiredService<IAnnotationService>();

        var subjectKey = AnnotationKey.CreateSubject("subject-for-context");
        await annotations.CreateAnnotationAsync(
            TestConstants.Organization,
            new Subject
            {
                AnnotationKey = subjectKey,
                AnnotationType = AnnotationType.Subject,
                Name = "subject-for-context",
                ProjectName = Constants.DefaultProjectName,
                ViewName = Constants.DefaultViewName,
            });

        var contextKey = AnnotationKey.CreateContext("subject-for-context", "simple-context");
        var key = FullKey.Create(contextKey, TestConstants.Organization, Constants.DefaultProjectName, Constants.DefaultViewName);
        await annotations.CreateAnnotationAsync(
            TestConstants.Organization,
            new Context
            {
                AnnotationKey = contextKey,
                AnnotationType = AnnotationType.Context,
                Name = "simple-context",
                SubjectKey = subjectKey,
                SubjectName = "subject-for-context",
                ProjectName = Constants.DefaultProjectName,
                ViewName = Constants.DefaultViewName,
            });

        var returned = await annotations.GetAnnotationAsync(key);
        var returnedSubject = (Subject)await annotations.GetAnnotationAsync(
            FullKey.Create(
                subjectKey,
                TestConstants.Organization,
                Constants.DefaultProjectName,
                Constants.DefaultViewName));

        returned.Should().BeOfType<Context>();
        var returnedTyped = (Context)returned;
        returnedTyped.Name.Should().Be("simple-context");
        returnedTyped.SubjectKey.Should().Be(subjectKey);

        returnedSubject.Contexts.Should().HaveCount(1);
        returnedSubject.Contexts.Should().Contain("simple-context");
    }

    [Fact]
    public async Task UsageAnnotation()
    {
        var annotations = Context.Services.GetRequiredService<IAnnotationService>();

        var subjectKey = AnnotationKey.CreateSubject("subject-for-usage");
        await annotations.CreateAnnotationAsync(
            TestConstants.Organization,
            new Subject
            {
                AnnotationKey = subjectKey,
                AnnotationType = AnnotationType.Subject,
                Name = "subject-for-usage",
                ProjectName = Constants.DefaultProjectName,
                ViewName = Constants.DefaultViewName,
            });

        var responsibilityKey = AnnotationKey.CreateResponsibility("responsibility-for-usage");
        await annotations.CreateAnnotationAsync(
            TestConstants.Organization,
            new Responsibility
            {
                AnnotationKey = responsibilityKey,
                AnnotationType = AnnotationType.Responsibility,
                Name = "responsibility-for-usage",
                ProjectName = Constants.DefaultProjectName,
                ViewName = Constants.DefaultViewName,
            });

        var usageKey = AnnotationKey.CreateUsage("subject-for-usage", "responsibility-for-usage");
        var key = FullKey.Create(usageKey, TestConstants.Organization, Constants.DefaultProjectName, Constants.DefaultViewName);
        await annotations.CreateAnnotationAsync(
            TestConstants.Organization,
            new Usage
            {
                AnnotationKey = usageKey,
                AnnotationType = AnnotationType.Usage,
                Name = "responsibility-for-usage",
                SubjectKey = subjectKey,
                SubjectName = "subject-for-usage",
                ResponsibilityKey = responsibilityKey,
                ResponsibilityName = "responsibility-for-usage",
                ProjectName = Constants.DefaultProjectName,
                ViewName = Constants.DefaultViewName,
            });

        var returned = await annotations.GetAnnotationAsync(key);
        var returnedSubject = (Subject)await annotations.GetAnnotationAsync(
            FullKey.Create(
                subjectKey,
                TestConstants.Organization,
                Constants.DefaultProjectName,
                Constants.DefaultViewName));

        returned.Should().BeOfType<Usage>();
        var returnedTyped = (Usage)returned;
        returnedTyped.Name.Should().Be("responsibility-for-usage");
        returnedTyped.SubjectKey.Should().Be(subjectKey);
        returnedTyped.ResponsibilityKey.Should().Be(responsibilityKey);

        returnedSubject.Usages.Should().HaveCount(1);
        returnedSubject.Usages.Should().Contain("responsibility-for-usage");

    }

    [Fact]
    public async Task ExecutionAnnotation()
    {
        var annotations = Context.Services.GetRequiredService<IAnnotationService>();

        var subjectKey = AnnotationKey.CreateSubject("subject-for-execution");
        await annotations.CreateAnnotationAsync(
            TestConstants.Organization,
            new Subject
            {
                AnnotationKey = subjectKey,
                AnnotationType = AnnotationType.Subject,
                Name = "subject-for-execution",
                ProjectName = Constants.DefaultProjectName,
                ViewName = Constants.DefaultViewName,
            });

        var responsibilityKey = AnnotationKey.CreateResponsibility("responsibility-for-execution");
        await annotations.CreateAnnotationAsync(
            TestConstants.Organization,
            new Responsibility
            {
                AnnotationKey = responsibilityKey,
                AnnotationType = AnnotationType.Responsibility,
                Name = "responsibility-for-execution",
                ProjectName = Constants.DefaultProjectName,
                ViewName = Constants.DefaultViewName,
            });

        var contextKey = AnnotationKey.CreateContext("subject-for-execution", "context-for-execution");
        await annotations.CreateAnnotationAsync(
            TestConstants.Organization,
            new Context
            {
                AnnotationKey = contextKey,
                AnnotationType = AnnotationType.Context,
                Name = "context-for-execution",
                SubjectKey = subjectKey,
                SubjectName = "subject-for-execution",
                ProjectName = Constants.DefaultProjectName,
                ViewName = Constants.DefaultViewName,
            });

        var usageKey = AnnotationKey.CreateUsage("subject-for-execution", "responsibility-for-execution");
        await annotations.CreateAnnotationAsync(
            TestConstants.Organization,
            new Usage
            {
                AnnotationKey = usageKey,
                AnnotationType = AnnotationType.Usage,
                Name = "responsibility-for-execution",
                SubjectKey = subjectKey,
                SubjectName = "subject-for-execution",
                ResponsibilityKey = responsibilityKey,
                ResponsibilityName = "responsibility-for-execution",
                ProjectName = Constants.DefaultProjectName,
                ViewName = Constants.DefaultViewName,
            });

        var executionKey = AnnotationKey.CreateExecution("subject-for-execution", "responsibility-for-execution", "context-for-execution");
        var key = FullKey.Create(executionKey, TestConstants.Organization, Constants.DefaultProjectName, Constants.DefaultViewName);
        await annotations.CreateAnnotationAsync(
            TestConstants.Organization,
            new Execution
            {
                AnnotationKey = executionKey,
                AnnotationType = AnnotationType.Execution,
                Name = "context-for-execution",
                SubjectKey = subjectKey,
                SubjectName = "subject-for-execution",
                ResponsibilityKey = responsibilityKey,
                ResponsibilityName = "responsibility-for-execution",
                ContextKey = contextKey,
                ContextName = "context-for-execution",
                ProjectName = Constants.DefaultProjectName,
                ViewName = Constants.DefaultViewName,
            });

        var returned = await annotations.GetAnnotationAsync(key);
        var returnedSubject = (Subject)await annotations.GetAnnotationAsync(
            FullKey.Create(subjectKey, TestConstants.Organization, Constants.DefaultProjectName, Constants.DefaultViewName));
        var returnedContext = (Context)await annotations.GetAnnotationAsync(
            FullKey.Create(contextKey, TestConstants.Organization, Constants.DefaultProjectName, Constants.DefaultViewName));
        var returnedUsage = (Usage)await annotations.GetAnnotationAsync(
            FullKey.Create(usageKey, TestConstants.Organization, Constants.DefaultProjectName, Constants.DefaultViewName));

        returned.Should().BeOfType<Execution>();
        var returnedTyped = (Execution)returned;
        returnedTyped.Name.Should().Be("context-for-execution");
        returnedTyped.SubjectKey.Should().Be(subjectKey);
        returnedTyped.ResponsibilityKey.Should().Be(responsibilityKey);
        returnedTyped.ContextKey.Should().Be(contextKey);

        returnedSubject.Contexts.Should().HaveCount(1);
        returnedSubject.Contexts.Should().Contain("context-for-execution");
        returnedSubject.Usages.Should().HaveCount(1);
        returnedSubject.Usages.Should().Contain("responsibility-for-execution");

        returnedContext.Executions.Should().HaveCount(1);
        returnedContext.Executions.Should().Contain("responsibility-for-execution");

        returnedUsage.Executions.Should().HaveCount(1);
        returnedUsage.Executions.Should().Contain("context-for-execution");
    }
}
