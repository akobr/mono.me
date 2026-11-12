namespace _42.Platform.Storyteller.Api.Functions.UnitTests;

// The Sentry SDK keeps its hub in a static: the tests that start it run one at a time and close it afterwards.
[CollectionDefinition(Name, DisableParallelization = true)]
public sealed class SentrySdkCollection
{
    public const string Name = "SentrySdk";
}
