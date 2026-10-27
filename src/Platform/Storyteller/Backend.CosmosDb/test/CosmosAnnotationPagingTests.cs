#nullable enable

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using _42.Platform.Storyteller.Annotating;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Shouldly;
using Xunit;

namespace _42.Platform.Storyteller.Backend.CosmosDb.UnitTests;

// Annotation lists follow their continuation token: with a page size of two, every list returns each annotation once
// and ends. Every test works in its own project.
public class CosmosAnnotationPagingTests(Startup startup)
    : BaseTestsClass(startup)
{
    private const string Organization = TestConstants.Organization;
    private const string View = Constants.DefaultViewName;
    private const int SmallPageSize = 2;

    // far more pages than any test needs, cross-partition queries with empty pages included: a list that does not end
    // fails instead of looping
    private const int MaxPages = 500;

    private IAnnotationService Annotations => Context.Services.GetRequiredService<IAnnotationService>();

    // a service of its own with the small page size; the shared one keeps the default
    private CosmosAnnotationService Paged => new(
        Context.Services.GetRequiredService<IContainerRepositoryProvider>(),
        Context.Services.GetRequiredService<AnnotationItemSerializer>(),
        Context.Services.GetRequiredService<AnnotationAuditStore>(),
        Context.Services.GetRequiredService<IOptions<AnnotationOptions>>(),
        Context.Services.GetRequiredService<TimeProvider>())
    {
        PageSize = SmallPageSize,
    };

    [Fact]
    public async Task TheExecutionsOfAResponsibility_PageThroughEveryExecutionOnce()
    {
        var project = NewProject();
        var executions = await CreateExecutionsAsync(project, "pg-r", 5);
        await CreateAsync(project, "exe.pg-s1.pg-other.pg-c");

        // expired executions fill Cosmos DB pages and are dropped from them, so some pages are short or empty
        var expired = DateTimeOffset.UtcNow.AddDays(-1);
        await CreateAsync(project, "exe.pg-x1.pg-r.pg-c", annotation => annotation with { ExpiresAt = expired });
        await CreateAsync(project, "exe.pg-x2.pg-r.pg-c", annotation => annotation with { ExpiresAt = expired });

        var request = Request(project, AnnotationType.Execution);
        request.DescendantsOf = AnnotationKey.Parse("rst.pg-r");
        var list = await ReadAllAsync(request);

        list.Keys.Order(StringComparer.Ordinal).ShouldBe(executions);
        list.Pages.ShouldBeGreaterThanOrEqualTo(3);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task TheExecutionList_PagesThroughEveryExecutionOnce(bool byResponsibility)
    {
        var project = NewProject();
        var executions = await CreateExecutionsAsync(project, "pg-r", 5);
        await CreateAsync(project, "exe.pg-s1.pg-other.pg-c");

        // as …/executions builds it: an exact responsibility name is the partition of that responsibility
        var request = Request(project, AnnotationType.Execution);
        request.PartitionKey = byResponsibility ? PartitionKeys.GetResponsibility(project, "pg-r") : null;
        var list = await ReadAllAsync(request);

        var expected = byResponsibility
            ? executions
            : executions.Append("exe.pg-s1.pg-other.pg-c").Order(StringComparer.Ordinal).ToList();
        list.Keys.Order(StringComparer.Ordinal).ShouldBe(expected);
        list.Pages.ShouldBeGreaterThanOrEqualTo(3);
    }

    [Fact]
    public async Task TheListOfEveryType_PagesAcrossTypeBoundaries_AndReturnsEveryAnnotationOnce()
    {
        var project = NewProject();

        // one responsibility, subject and usage, and three contexts and executions: nine annotations of five types
        foreach (var context in new[] { "pg-c1", "pg-c2", "pg-c3" })
        {
            await CreateAsync(project, $"exe.pg-s.pg-r.{context}");
        }

        var request = Request(project, Enum.GetValues<AnnotationType>());
        var list = await ReadAllAsync(request);

        list.Keys.Order(StringComparer.Ordinal).ShouldBe(new[]
        {
            "cnt.pg-s.pg-c1",
            "cnt.pg-s.pg-c2",
            "cnt.pg-s.pg-c3",
            "exe.pg-s.pg-r.pg-c1",
            "exe.pg-s.pg-r.pg-c2",
            "exe.pg-s.pg-r.pg-c3",
            "rst.pg-r",
            "sbt.pg-s",
            "usg.pg-s.pg-r",
        });
        list.TokenTypes.Distinct().Count().ShouldBeGreaterThan(1, "pages end in more than one type");
    }

    private static string NewProject() => $"pag-{Guid.NewGuid():N}"[..16];

    private static AnnotationsRequest Request(string project, params AnnotationType[] types)
    {
        return new AnnotationsRequest
        {
            Organization = Organization,
            Project = project,
            View = View,
            Types = types,
        };
    }

    // Executions of the responsibility in one context, one subject each; returns their keys in ordinal order.
    private async Task<List<string>> CreateExecutionsAsync(string project, string responsibility, int count)
    {
        var keys = Enumerable.Range(1, count)
            .Select(index => $"exe.pg-s{index}.{responsibility}.pg-c")
            .Order(StringComparer.Ordinal)
            .ToList();

        foreach (var key in keys)
        {
            await CreateAsync(project, key);
        }

        return keys;
    }

    private Task CreateAsync(string project, string key, Func<Annotation, Annotation>? change = null)
    {
        var model = AnnotationFactory.CreateDefault(AnnotationKey.Parse(key), project, View);
        return Annotations.CreateAnnotationAsync(Organization, change?.Invoke(model) ?? model);
    }

    // Follows the continuation token to the end, as a client does.
    private async Task<PagedList> ReadAllAsync(AnnotationsRequest request)
    {
        var service = Paged;
        var keys = new List<string>();
        var tokenTypes = new List<string>();
        var pages = 0;

        do
        {
            pages++;
            pages.ShouldBeLessThanOrEqualTo(MaxPages, "the list does not end");

            var page = await service.GetAnnotationsAsync(request);
            keys.AddRange(page.Annotations.Select(annotation => annotation.AnnotationKey));
            request.ContinuationToken = page.ContinuationToken;

            if (page.ContinuationToken is not null)
            {
                ContinuationToken.TryParse(page.ContinuationToken, out var token).ShouldBeTrue();
                tokenTypes.Add(token!.Type);
            }
        }
        while (request.ContinuationToken is not null);

        return new PagedList(keys, tokenTypes, pages);
    }

    private sealed record PagedList(List<string> Keys, List<string> TokenTypes, int Pages);
}
