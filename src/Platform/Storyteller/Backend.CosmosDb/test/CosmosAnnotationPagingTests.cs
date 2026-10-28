#nullable enable

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using _42.Platform.Storyteller.Annotating;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
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

        list.Keys.Order(StringComparer.Ordinal).Should().Equal(executions);
        list.Pages.Should().BeGreaterThanOrEqualTo(3);
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
        list.Keys.Order(StringComparer.Ordinal).Should().Equal(expected);
        list.Pages.Should().BeGreaterThanOrEqualTo(3);
    }

    [Fact]
    public async Task TheListOfEveryType_PagesAcrossTypeBoundaries_AndReturnsEveryAnnotationOnce()
    {
        var project = NewProject();
        await CreateContextsAsync(project);

        var request = Request(project, Enum.GetValues<AnnotationType>());
        var list = await ReadAllAsync(request);

        list.Keys.Order(StringComparer.Ordinal).Should().Equal(
            "cnt.pg-s.pg-c1",
            "cnt.pg-s.pg-c2",
            "cnt.pg-s.pg-c3",
            "exe.pg-s.pg-r.pg-c1",
            "exe.pg-s.pg-r.pg-c2",
            "exe.pg-s.pg-r.pg-c3",
            "rst.pg-r",
            "sbt.pg-s",
            "usg.pg-s.pg-r");
        list.TokenTypes.Distinct().Count().Should().BeGreaterThan(1, "pages end in more than one type");
    }

    [Fact]
    public async Task TheTokenOfATypeBoundary_StartsTheListAtTheFirstPageOfItsType()
    {
        var project = NewProject();
        await CreateContextsAsync(project);

        // the token a page ends with when it fills up before a type starts; it carries no Cosmos DB token, so the
        // contexts start at their first page and the types before them are skipped
        var request = Request(project, Enum.GetValues<AnnotationType>());
        request.ContinuationToken = ContinuationToken.CreateFirstPage(nameof(AnnotationType.Context)).ToString();
        var list = await ReadAllAsync(request);

        list.Keys.Order(StringComparer.Ordinal).Should().Equal(
            "cnt.pg-s.pg-c1",
            "cnt.pg-s.pg-c2",
            "cnt.pg-s.pg-c3",
            "exe.pg-s.pg-r.pg-c1",
            "exe.pg-s.pg-r.pg-c2",
            "exe.pg-s.pg-r.pg-c3");
    }

    [Fact]
    public async Task ATokenWhoseCosmosPartIsNotACosmosToken_IsInvalidQuery()
    {
        var project = NewProject();
        await CreateExecutionsAsync(project, "pg-r", 3);

        // decodes as a token of the executions, but Cosmos DB cannot read its own part
        var request = Request(project, AnnotationType.Execution);
        request.ContinuationToken = new ContinuationToken(nameof(AnnotationType.Execution), "not a cosmos token").ToString();
        var act = () => Paged.GetAnnotationsAsync(request);

        (await act.Should().ThrowAsync<InvalidInputException>()).Which.ErrorCode.Should().Be(ErrorCodes.InvalidQuery);
    }

    [Fact]
    public async Task ADeletedListTokenThatCosmosRejects_IsInvalidQuery_WithoutTheCosmosMessage()
    {
        var project = NewProject();
        await CreateAsync(project, "exe.pg-s.pg-r.pg-c");

        // the deleted list passes its token to Cosmos DB as it is; a plain CosmosException would answer 500 with the
        // Cosmos DB message, which quotes the token
        var act = () => Annotations.GetDeletedAnnotationsAsync(Organization, project, View, continuationToken: "not-a-cosmos-token");

        var exception = (await act.Should().ThrowAsync<InvalidInputException>()).Which;
        exception.ErrorCode.Should().Be(ErrorCodes.InvalidQuery);
        exception.Message.Should().Be("The continuation token is not valid; list again without it.");
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

    // An execution in each of three contexts, with its ancestors: one responsibility, subject and usage, and three
    // contexts and executions, nine annotations of five types.
    private async Task CreateContextsAsync(string project)
    {
        foreach (var context in new[] { "pg-c1", "pg-c2", "pg-c3" })
        {
            await CreateAsync(project, $"exe.pg-s.pg-r.{context}");
        }
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
            pages.Should().BeLessThanOrEqualTo(MaxPages, "the list ends");

            var page = await service.GetAnnotationsAsync(request);
            keys.AddRange(page.Annotations.Select(annotation => annotation.AnnotationKey));
            request.ContinuationToken = page.ContinuationToken;

            if (page.ContinuationToken is not null)
            {
                ContinuationToken.TryParse(page.ContinuationToken, out var token).Should().BeTrue();
                tokenTypes.Add(token!.Type);
            }
        }
        while (request.ContinuationToken is not null);

        return new PagedList(keys, tokenTypes, pages);
    }

    private sealed record PagedList(List<string> Keys, List<string> TokenTypes, int Pages);
}
