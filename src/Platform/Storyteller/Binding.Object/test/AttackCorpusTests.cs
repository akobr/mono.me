using System;
using System.Diagnostics;
using System.Threading.Tasks;
using _42.Platform.Storyteller.Binding;
using FluentAssertions;
using Newtonsoft.Json.Linq;
using Xunit;
using Xunit.Abstractions;

namespace _42.Platform.Storyteller.Binding.Object.UnitTests;

/// <summary>
/// Every input from <see cref="AttackCorpus"/> must stop on an evaluation limit, quickly, under the default limits.
/// </summary>
[Collection(AttackCorpusCollection.Name)]
public class AttackCorpusTests
{
    private static readonly TimeSpan MaxWallTime = TimeSpan.FromSeconds(2);

    private readonly ITestOutputHelper _output;
    private readonly ConfigurationBindingResolver _resolver = new(new FakeStringBinding());
    private readonly BindingScope _scope = new() { Document = new JObject() };

    public AttackCorpusTests(ITestOutputHelper output)
    {
        _output = output;
    }

    [Theory]
    [MemberData(nameof(AttackCorpus.Names), MemberType = typeof(AttackCorpus))]
    public async Task Vector_StopsOnLimit(string name)
    {
        var content = AttackCorpus.Build(name);
        var stopwatch = Stopwatch.StartNew();

        var act = () => _resolver.ResolveAsync(content, includeSecrets: true, _scope).AsTask();

        var thrown = await act.Should().ThrowAsync<EvaluationLimitExceededException>();
        stopwatch.Stop();
        _output.WriteLine($"{name}: {thrown.Which.Kind} after {stopwatch.ElapsedMilliseconds} ms");
        stopwatch.Elapsed.Should().BeLessThan(MaxWallTime);
    }
}
