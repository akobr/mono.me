using System.Collections.Generic;
using System.Linq;
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
        returnedTyped.ContextNames.Should().BeEmpty();
        returnedTyped.ResponsibilityNames.Should().BeEmpty();
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

        returnedSubject.ContextNames.Should().HaveCount(1);
        returnedSubject.ContextNames.Should().Contain("simple-context");
    }

    [Fact]
    public async Task ContextOnlyAnnotation()
    {
        var annotations = Context.Services.GetRequiredService<IAnnotationService>();
        var contextKey = AnnotationKey.CreateContext("subject-for-only-context", "only-context");
        var subjectKey = AnnotationKey.CreateSubject("subject-for-only-context");

        var createdAnnotations = (await annotations.CreateAnnotationAsync(
            TestConstants.Organization,
            new Context
            {
                AnnotationKey = contextKey,
                AnnotationType = AnnotationType.Context,
                Name = "only-context",
                SubjectKey = subjectKey,
                SubjectName = "subject-for-only-context",
                ProjectName = Constants.DefaultProjectName,
                ViewName = Constants.DefaultViewName,
            })).ToList();

        createdAnnotations.Should().HaveCount(2);
        createdAnnotations[0].AnnotationKey.Should().Be(subjectKey);
        createdAnnotations[0].AnnotationType.Should().Be(AnnotationType.Subject);
        createdAnnotations[1].AnnotationKey.Should().Be(contextKey);
        createdAnnotations[1].AnnotationType.Should().Be(AnnotationType.Context);
        var subject = (Subject)createdAnnotations[0];
        subject.ContextNames.Should().HaveCount(1);
        subject.ContextNames.Should().Contain("only-context");
    }

    [Fact]
    public async Task UnitOnlyAnnotation()
    {
        var annotations = Context.Services.GetRequiredService<IAnnotationService>();
        var unitKey = AnnotationKey.CreateUnit("responsibility-for-only-unit", "only-unit");
        var responsibilityKey = AnnotationKey.CreateResponsibility("responsibility-for-only-unit");

        var createdAnnotations = (await annotations.CreateAnnotationAsync(
            TestConstants.Organization,
            new Unit
            {
                AnnotationKey = unitKey,
                AnnotationType = AnnotationType.Unit,
                Name = "only-unit",
                ResponsibilityKey = responsibilityKey,
                ResponsibilityName = "responsibility-for-only-unit",
                UnitType = "event",
                UnitDefinition = "unknown",
                ProjectName = Constants.DefaultProjectName,
                ViewName = Constants.DefaultViewName,
            })).ToList();

        createdAnnotations.Should().HaveCount(2);
        createdAnnotations[0].AnnotationKey.Should().Be(responsibilityKey);
        createdAnnotations[0].AnnotationType.Should().Be(AnnotationType.Responsibility);
        createdAnnotations[1].AnnotationKey.Should().Be(unitKey);
        createdAnnotations[1].AnnotationType.Should().Be(AnnotationType.Unit);
        var responsibility = (Responsibility)createdAnnotations[0];
        responsibility.UnitNames.Should().HaveCount(1);
        responsibility.UnitNames.Should().Contain("only-unit");
    }

    [Fact]
    public async Task UsageOnlyAnnotation()
    {
        var annotations = Context.Services.GetRequiredService<IAnnotationService>();
        var usageKey = AnnotationKey.CreateUsage("subject-for-only-usage", "responsibility-for-only-usage");
        var subjectKey = AnnotationKey.CreateSubject("subject-for-only-usage");
        var responsibilityKey = AnnotationKey.CreateResponsibility("responsibility-for-only-usage");

        var createdAnnotations = (await annotations.CreateAnnotationAsync(
            TestConstants.Organization,
            new Usage
            {
                AnnotationKey = usageKey,
                AnnotationType = AnnotationType.Usage,
                Name = "responsibility-for-only-usage",
                SubjectKey = subjectKey,
                SubjectName = "subject-for-only-usage",
                ResponsibilityKey = responsibilityKey,
                ResponsibilityName = "responsibility-for-only-usage",
                ProjectName = Constants.DefaultProjectName,
                ViewName = Constants.DefaultViewName,
            })).ToList();

        createdAnnotations.Should().HaveCount(3);
        createdAnnotations.Should().Contain(a => a.AnnotationKey == responsibilityKey && a.AnnotationType == AnnotationType.Responsibility);
        createdAnnotations.Should().Contain(a => a.AnnotationKey == subjectKey && a.AnnotationType == AnnotationType.Subject);
        createdAnnotations.Should().Contain(a => a.AnnotationKey == usageKey && a.AnnotationType == AnnotationType.Usage);

        var subject = (Subject)createdAnnotations.First(a => a.AnnotationKey == subjectKey);
        subject.ResponsibilityNames.Should().HaveCount(1);
        subject.ResponsibilityNames.Should().Contain("responsibility-for-only-usage");
    }

    [Fact]
    public async Task ExecutionOnlyAnnotation()
    {
        var annotations = Context.Services.GetRequiredService<IAnnotationService>();
        var executionKey = AnnotationKey.CreateExecution("subject-for-only-execution", "responsibility-for-only-execution", "context-for-only-execution");
        var subjectKey = AnnotationKey.CreateSubject("subject-for-only-execution");
        var responsibilityKey = AnnotationKey.CreateResponsibility("responsibility-for-only-execution");
        var contextKey = AnnotationKey.CreateContext("subject-for-only-execution", "context-for-only-execution");
        var usageKey = AnnotationKey.CreateUsage("subject-for-only-execution", "responsibility-for-only-execution");

        var createdAnnotations = (await annotations.CreateAnnotationAsync(
            TestConstants.Organization,
            new Execution
            {
                AnnotationKey = executionKey,
                AnnotationType = AnnotationType.Execution,
                Name = "context-for-only-execution",
                SubjectKey = subjectKey,
                SubjectName = "subject-for-only-execution",
                ResponsibilityKey = responsibilityKey,
                ResponsibilityName = "responsibility-for-only-execution",
                ContextKey = contextKey,
                ContextName = "context-for-only-execution",
                UsageKey = usageKey,
                ProjectName = Constants.DefaultProjectName,
                ViewName = Constants.DefaultViewName,
            })).ToList();

        createdAnnotations.Should().HaveCount(5);
        createdAnnotations.Should().Contain(a => a.AnnotationKey == responsibilityKey && a.AnnotationType == AnnotationType.Responsibility);
        createdAnnotations.Should().Contain(a => a.AnnotationKey == subjectKey && a.AnnotationType == AnnotationType.Subject);
        createdAnnotations.Should().Contain(a => a.AnnotationKey == usageKey && a.AnnotationType == AnnotationType.Usage);
        createdAnnotations.Should().Contain(a => a.AnnotationKey == contextKey && a.AnnotationType == AnnotationType.Context);
        createdAnnotations.Should().Contain(a => a.AnnotationKey == executionKey && a.AnnotationType == AnnotationType.Execution);

        var subject = (Subject)createdAnnotations.First(a => a.AnnotationKey == subjectKey);
        subject.ContextNames.Should().Contain("context-for-only-execution");
        subject.ResponsibilityNames.Should().Contain("responsibility-for-only-execution");

        var context = (Context)createdAnnotations.First(a => a.AnnotationKey == contextKey);
        context.ResponsibilityNames.Should().Contain("responsibility-for-only-execution");

        var usage = (Usage)createdAnnotations.First(a => a.AnnotationKey == usageKey);
        usage.ContextNames.Should().Contain("context-for-only-execution");
    }

    [Fact]
    public async Task UnitOfExecutionOnlyAnnotation()
    {
        var annotations = Context.Services.GetRequiredService<IAnnotationService>();
        var unitOfExecutionKey = AnnotationKey.CreateUnitOfExecution("subject-for-only-uoe", "responsibility-for-only-uoe", "context-for-only-uoe", "only-uoe");
        var subjectKey = AnnotationKey.CreateSubject("subject-for-only-uoe");
        var responsibilityKey = AnnotationKey.CreateResponsibility("responsibility-for-only-uoe");
        var contextKey = AnnotationKey.CreateContext("subject-for-only-uoe", "context-for-only-uoe");
        var usageKey = AnnotationKey.CreateUsage("subject-for-only-uoe", "responsibility-for-only-uoe");
        var executionKey = AnnotationKey.CreateExecution("subject-for-only-uoe", "responsibility-for-only-uoe", "context-for-only-uoe");
        var unitKey = AnnotationKey.CreateUnit("responsibility-for-only-uoe", "only-uoe");

        var createdAnnotations = (await annotations.CreateAnnotationAsync(
            TestConstants.Organization,
            new UnitOfExecution
            {
                AnnotationKey = unitOfExecutionKey,
                AnnotationType = AnnotationType.UnitOfExecution,
                Name = "only-uoe",
                SubjectKey = subjectKey,
                SubjectName = "subject-for-only-uoe",
                ResponsibilityKey = responsibilityKey,
                ResponsibilityName = "responsibility-for-only-uoe",
                ContextKey = contextKey,
                ContextName = "context-for-only-uoe",
                UnitKey = unitKey,
                UnitName = "only-uoe",
                UsageKey = usageKey,
                ExecutionKey = executionKey,
                ProjectName = Constants.DefaultProjectName,
                ViewName = Constants.DefaultViewName,
            })).ToList();

        createdAnnotations.Should().HaveCount(7);
        createdAnnotations.Should().Contain(a => a.AnnotationKey == responsibilityKey && a.AnnotationType == AnnotationType.Responsibility);
        createdAnnotations.Should().Contain(a => a.AnnotationKey == subjectKey && a.AnnotationType == AnnotationType.Subject);
        createdAnnotations.Should().Contain(a => a.AnnotationKey == usageKey && a.AnnotationType == AnnotationType.Usage);
        createdAnnotations.Should().Contain(a => a.AnnotationKey == contextKey && a.AnnotationType == AnnotationType.Context);
        createdAnnotations.Should().Contain(a => a.AnnotationKey == executionKey && a.AnnotationType == AnnotationType.Execution);
        createdAnnotations.Should().Contain(a => a.AnnotationKey == unitKey && a.AnnotationType == AnnotationType.Unit);
        createdAnnotations.Should().Contain(a => a.AnnotationKey == unitOfExecutionKey && a.AnnotationType == AnnotationType.UnitOfExecution);

        var execution = (Execution)createdAnnotations.First(a => a.AnnotationKey == executionKey);
        execution.UnitNames.Should().Contain("only-uoe");

        var responsibility = (Responsibility)createdAnnotations.First(a => a.AnnotationKey == responsibilityKey);
        responsibility.UnitNames.Should().Contain("only-uoe");
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

        returnedSubject.ResponsibilityNames.Should().HaveCount(1);
        returnedSubject.ResponsibilityNames.Should().Contain("responsibility-for-usage");

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
                UsageKey = usageKey,
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
        returnedTyped.UsageKey.Should().Be(usageKey);

        returnedSubject.ContextNames.Should().HaveCount(1);
        returnedSubject.ContextNames.Should().Contain("context-for-execution");
        returnedSubject.ResponsibilityNames.Should().HaveCount(1);
        returnedSubject.ResponsibilityNames.Should().Contain("responsibility-for-execution");

        returnedContext.ResponsibilityNames.Should().HaveCount(1);
        returnedContext.ResponsibilityNames.Should().Contain("responsibility-for-execution");

        returnedUsage.ContextNames.Should().HaveCount(1);
        returnedUsage.ContextNames.Should().Contain("context-for-execution");
    }

    [Fact]
    public async Task CreateAnnotationsFromString()
    {
        var annotations = Context.Services.GetRequiredService<IAnnotationService>();

        var input = new List<string>
        {
            "sbt.subject-explicit",
            "rst.responsibility-explicit",
            "subject-default",
            "subject-default.responsibility-default",
            "subject-default.responsibility-default.context-default",
            "subject-default.responsibility-default.context-default.unit-default",
            "too.many.segments.in.this.input.should.be.ignored",
        };

        var created = (await annotations.CreateAnnotationsFromStringAsync(TestConstants.Organization, input)).ToList();

        created.Any(a => a.AnnotationKey.ToString() == "sbt.subject-explicit").Should().BeTrue();
        created.Any(a => a.AnnotationKey.ToString() == "rst.responsibility-explicit").Should().BeTrue();
        created.Any(a => a.AnnotationKey.ToString() == "sbt.subject-default").Should().BeTrue();
        created.Any(a => a.AnnotationKey.ToString() == "usg.subject-default.responsibility-default").Should().BeTrue();
        created.Any(a => a.AnnotationKey.ToString() == "exe.subject-default.responsibility-default.context-default").Should().BeTrue();
        created.Any(a => a.AnnotationKey.ToString() == "uxe.subject-default.responsibility-default.context-default.unit-default").Should().BeTrue();

        // Check that ancestors are created (via service as they might be created via patches and not returned in the list)
        var responsibilityDefault = await annotations.GetAnnotationAsync(FullKey.Create(AnnotationKey.Parse("rst.responsibility-default"), TestConstants.Organization, Constants.DefaultProjectName, Constants.DefaultViewName));
        responsibilityDefault.Should().NotBeNull();

        var contextDefault = await annotations.GetAnnotationAsync(FullKey.Create(AnnotationKey.Parse("cnt.subject-default.context-default"), TestConstants.Organization, Constants.DefaultProjectName, Constants.DefaultViewName));
        contextDefault.Should().NotBeNull();

        var unitDefault = await annotations.GetAnnotationAsync(FullKey.Create(AnnotationKey.Parse("unt.responsibility-default.unit-default"), TestConstants.Organization, Constants.DefaultProjectName, Constants.DefaultViewName));
        unitDefault.Should().NotBeNull();
    }

    [Fact]
    public async Task SetAnnotationsBulk()
    {
        var annotations = Context.Services.GetRequiredService<IAnnotationService>();

        var subjectKey = AnnotationKey.CreateSubject("bulk-subject");
        var responsibilityKey = AnnotationKey.CreateResponsibility("bulk-responsibility");
        var contextKey = AnnotationKey.CreateContext("bulk-subject", "bulk-context");
        var usageKey = AnnotationKey.CreateUsage("bulk-subject", "bulk-responsibility");
        var executionKey = AnnotationKey.CreateExecution("bulk-subject", "bulk-responsibility", "bulk-context");

        var list = new List<Annotation>
        {
            new Execution
            {
                AnnotationKey = executionKey,
                AnnotationType = AnnotationType.Execution,
                Name = "bulk-context",
                SubjectKey = subjectKey,
                SubjectName = "bulk-subject",
                ResponsibilityKey = responsibilityKey,
                ResponsibilityName = "bulk-responsibility",
                ContextKey = contextKey,
                ContextName = "bulk-context",
                UsageKey = usageKey,
                ProjectName = Constants.DefaultProjectName,
                ViewName = Constants.DefaultViewName,
            },
            new Context
            {
                AnnotationKey = contextKey,
                AnnotationType = AnnotationType.Context,
                Name = "bulk-context",
                SubjectKey = subjectKey,
                SubjectName = "bulk-subject",
                ProjectName = Constants.DefaultProjectName,
                ViewName = Constants.DefaultViewName,
            },
            new Usage
            {
                AnnotationKey = usageKey,
                AnnotationType = AnnotationType.Usage,
                Name = "bulk-responsibility",
                SubjectKey = subjectKey,
                SubjectName = "bulk-subject",
                ResponsibilityKey = responsibilityKey,
                ResponsibilityName = "bulk-responsibility",
                ProjectName = Constants.DefaultProjectName,
                ViewName = Constants.DefaultViewName,
            },
            new Subject
            {
                AnnotationKey = subjectKey,
                AnnotationType = AnnotationType.Subject,
                Name = "bulk-subject",
                ProjectName = Constants.DefaultProjectName,
                ViewName = Constants.DefaultViewName,
            },
            new Responsibility
            {
                AnnotationKey = responsibilityKey,
                AnnotationType = AnnotationType.Responsibility,
                Name = "bulk-responsibility",
                ProjectName = Constants.DefaultProjectName,
                ViewName = Constants.DefaultViewName,
            },
        };

        // This should succeed because SetAnnotationsAsync should sort them correctly
        await annotations.CreateAnnotationsAsync(TestConstants.Organization, list);

        var returnedSubject = (Subject)await annotations.GetAnnotationAsync(
            FullKey.Create(subjectKey, TestConstants.Organization, Constants.DefaultProjectName, Constants.DefaultViewName));
        var returnedContext = (Context)await annotations.GetAnnotationAsync(
            FullKey.Create(contextKey, TestConstants.Organization, Constants.DefaultProjectName, Constants.DefaultViewName));
        var returnedUsage = (Usage)await annotations.GetAnnotationAsync(
            FullKey.Create(usageKey, TestConstants.Organization, Constants.DefaultProjectName, Constants.DefaultViewName));

        returnedSubject.Should().NotBeNull();
        returnedSubject.ContextNames.Should().Contain("bulk-context");
        returnedSubject.ResponsibilityNames.Should().Contain("bulk-responsibility");
        returnedContext.ResponsibilityNames.Should().Contain("bulk-responsibility");
        returnedUsage.ContextNames.Should().Contain("bulk-context");
    }
}
