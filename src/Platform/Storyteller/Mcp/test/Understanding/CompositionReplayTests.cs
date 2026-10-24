using _42.Platform.Storyteller.Mcp.Understanding;

using Newtonsoft.Json.Linq;

namespace _42.Platform.Storyteller.Mcp.UnitTests.Understanding;

public class CompositionReplayTests
{
    [Fact]
    public void Replay_RebuildsTheContentOfTheComposition()
    {
        var composition = Compositions.Execution();

        var result = CompositionReplay.Run(composition.Nodes, CompositionReplay.StepsOf(composition));

        JToken.DeepEquals(result.Content, composition.Content()).ShouldBeTrue(result.Content.ToString());
    }

    [Fact]
    public void Replay_FindsTheStepOfEveryOrigin()
    {
        var composition = Compositions.Execution();

        var result = CompositionReplay.Run(composition.Nodes, CompositionReplay.StepsOf(composition));

        foreach (var origin in composition.Origins)
        {
            result.OriginOf(JsonPointer.Find(result.Content, origin.Path)!)!.Order.ShouldBe(origin.Step, origin.Path);
        }
    }

    [Fact]
    public void Replay_MergesFinishedDocuments_NotTheirParts()
    {
        // inheritance.md "Replaying": the usage brings a: {x: 1}; the context's template a: 5 is replaced by its own
        // a: {y: 2}, so the context's document is a: {y: 2}, and merging it keeps x
        var composition = Compositions.Execution();
        composition.SourceContent(Compositions.Usage)["a"] = new JObject { ["x"] = 1 };
        composition.SourceContent(Compositions.Context)["a"] = new JObject { ["y"] = 2 };
        var steps = CompositionReplay.StepsOf(composition);
        steps.Insert(4, new ReplayStep(null, "template:cnt", 4, new JObject { ["a"] = 5 }));

        var result = CompositionReplay.Run(composition.Nodes, steps);

        result.Content["a"]!.ToString(Newtonsoft.Json.Formatting.None).ShouldBe("""{"x":1,"y":2}""");
    }

    [Fact]
    public void Via_FollowsMergedIntoToTheTarget()
    {
        var composition = Compositions.Execution();

        CompositionReplay.Via(composition.Nodes, 3).ShouldBe(["sbt.northwind", "cnt.northwind.prod", Compositions.ExecutionKey]);
        CompositionReplay.Via(composition.Nodes, 5).ShouldBe([Compositions.ExecutionKey]);
    }
}
