using Xunit;

namespace _42.Platform.Storyteller.Binding.Object.UnitTests;

/// <summary>
/// Runs <see cref="AttackCorpusTests"/> alone, so wall-clock limits and timings are not distorted by parallel tests.
/// </summary>
[CollectionDefinition(Name, DisableParallelization = true)]
public sealed class AttackCorpusCollection
{
    public const string Name = "Attack corpus";
}
