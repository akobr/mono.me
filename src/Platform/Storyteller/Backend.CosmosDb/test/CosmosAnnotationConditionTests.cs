using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using _42.Platform.Storyteller.Annotating;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace _42.Platform.Storyteller.Backend.CosmosDb.UnitTests;

// The conditions of a list request are over the models and filter the mapped page of their own type (424-97: they
// were dropped for every type but rst, where a cast to the entity type failed).
public class CosmosAnnotationConditionTests(Startup startup)
    : BaseTestsClass(startup)
{
    private const string Project = "conditions";

    private IAnnotationService Annotations => Context.Services.GetRequiredService<IAnnotationService>();

    [Fact]
    public async Task List_ResponsibilitiesWithAPattern_ReturnsTheMatchingOnes()
    {
        await CreateAsync("usg.rp-north.rp-invoicing", "usg.rp-north.rp-shipping");
        var pattern = new Regex("^rp-inv", RegexOptions.None, TimeSpan.FromSeconds(1));

        var keys = await ListAsync(
            AnnotationType.Responsibility,
            new AnnotationsRequest.Condition<Responsibility> { Predicate = r => pattern.IsMatch(r.Name) });

        keys.Should().Equal("rst.rp-invoicing");
    }

    [Fact]
    public async Task List_UsagesBySubjectAndResponsibility_CombinesTheConditions()
    {
        await CreateAsync("usg.uc-north.uc-invoicing", "usg.uc-north.uc-shipping", "usg.uc-south.uc-invoicing");

        var bySubject = await ListAsync(
            AnnotationType.Usage,
            new AnnotationsRequest.Condition<Usage> { Predicate = u => u.SubjectName.Contains("uc-nor") });
        var byBoth = await ListAsync(
            AnnotationType.Usage,
            new AnnotationsRequest.Condition<Usage> { Predicate = u => u.SubjectName.Contains("uc-nor") },
            new AnnotationsRequest.Condition<Usage> { Predicate = u => u.ResponsibilityName == "uc-shipping" });

        bySubject.Should().BeEquivalentTo("usg.uc-north.uc-invoicing", "usg.uc-north.uc-shipping");
        byBoth.Should().Equal("usg.uc-north.uc-shipping");
    }

    [Fact]
    public async Task List_ContextsInThePartitionOfASubject_ReturnsTheExactName()
    {
        await CreateAsync("cnt.cx-north.cx-production", "cnt.cx-north.cx-staging", "cnt.cx-south.cx-production");

        var keys = await ListAsync(
            [AnnotationType.Context],
            PartitionKeys.GetSubject(Project, "cx-north"),
            new AnnotationsRequest.Condition<Context> { Predicate = c => c.Name == "cx-production" });

        keys.Should().Equal("cnt.cx-north.cx-production");
    }

    // A request for several types applies each condition to its own type only.
    [Fact]
    public async Task List_SeveralTypes_AppliesEachConditionToItsType()
    {
        await CreateAsync("usg.mt-north.mt-invoicing", "cnt.mt-north.mt-production", "cnt.mt-north.mt-staging");

        var keys = await ListAsync(
            [AnnotationType.Subject, AnnotationType.Usage, AnnotationType.Context],
            null,
            new AnnotationsRequest.Condition<Usage> { Predicate = _ => false },
            new AnnotationsRequest.Condition<Context> { Predicate = c => c.Name == "mt-production" });

        // the subjects have no condition, so the subjects of the other tests of the project come too
        keys.Where(key => key.Contains(".mt-", StringComparison.Ordinal)).Should().BeEquivalentTo("sbt.mt-north", "cnt.mt-north.mt-production");
    }

    private async Task CreateAsync(params string[] keys)
    {
        foreach (var key in keys)
        {
            var model = AnnotationFactory.CreateDefault(AnnotationKey.Parse(key), Project, Constants.DefaultViewName);
            await Annotations.CreateAnnotationAsync(TestConstants.Organization, model);
        }
    }

    private Task<List<string>> ListAsync(AnnotationType type, params AnnotationsRequest.ICondition[] conditions)
    {
        return ListAsync([type], null, conditions);
    }

    // The keys of every page: a filtered page can be short or empty and still have a token.
    private async Task<List<string>> ListAsync(AnnotationType[] types, string partitionKey, params AnnotationsRequest.ICondition[] conditions)
    {
        var keys = new List<string>();
        var request = new AnnotationsRequest
        {
            Organization = TestConstants.Organization,
            Project = Project,
            View = Constants.DefaultViewName,
            Types = types,
            PartitionKey = partitionKey,
            Conditions = conditions,
        };

        do
        {
            var page = await Annotations.GetAnnotationsAsync(request);
            keys.AddRange(page.Annotations.Select(a => a.AnnotationKey));
            request.ContinuationToken = page.ContinuationToken;
        }
        while (request.ContinuationToken is not null);

        return keys;
    }
}
