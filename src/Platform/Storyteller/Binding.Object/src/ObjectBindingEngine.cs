using System.Text.Json.Nodes;
using Json.JsonE;
using Json.Logic;
using Json.Logic.Rules;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json.Linq;

namespace _42.Platform.Storyteller.Binding.Object;

internal static class ObjectBindingEngine
{
    public const string JsonLogicKind = "jlogic";

    public const string JsonEKind = "jsone";

    static ObjectBindingEngine()
    {
        // LogRule.Logger falls back to a console logger. Pin our sink before any rule runs.
        LogRule.Logger = JsonLogicDebugLogger.Instance;
        JsonLogicBoundedRules.Register();
    }

    public static void UseLogger(ILogger? logger)
    {
        JsonLogicDebugLogger.Instance.Use(logger);
    }

    public static JToken Evaluate(
        string kind,
        JToken definition,
        JObject context,
        ConfigurationBindingResolver.EvaluationBudget budget)
    {
        try
        {
            if (kind == JsonEKind)
            {
                return EvaluateJsonE(definition, context, budget);
            }

            var definitionNode = JsonTokenConverter.ToNode(definition);
            if (definitionNode is null)
            {
                return JValue.CreateNull();
            }

            var contextNode = JsonTokenConverter.ToNode(context)
                ?? throw new BindingEvaluationException("Object binding $context must be a JSON object.");

            JsonLogicBoundedRules.UseBudget(budget);
            JsonNode? result = kind switch
            {
                JsonLogicKind => JsonLogic.Apply(definitionNode, contextNode),
                _ => throw new BindingEvaluationException($"Unknown object binding '{kind}'."),
            };

            EnsureResultFits(result);
            return JsonTokenConverter.ToToken(result);
        }
        catch (BindingException)
        {
            throw;
        }
        catch (JsonEException exception)
        {
            throw new BindingEvaluationException(exception.Message, exception);
        }
        catch (JsonLogicException exception)
        {
            throw new BindingEvaluationException(exception.Message, exception);
        }
    }

    private static JToken EvaluateJsonE(
        JToken definition,
        JObject context,
        ConfigurationBindingResolver.EvaluationBudget budget)
    {
        var prepared = JsonELimits.PrepareDefinition(definition);
        var definitionNode = JsonTokenConverter.ToNode(prepared);
        if (definitionNode is null)
        {
            return JValue.CreateNull();
        }

        if (JsonTokenConverter.ToNode(context) is not JsonObject contextNode)
        {
            throw new BindingEvaluationException("Object binding $context must be a JSON object.");
        }

        // A context function overrides the built-in of the same name, including one supplied by the caller.
        var sizeBudget = new JsonESizeBudget(budget);
        contextNode["range"] = JsonFunction.Create(JsonELimits.Range);
        contextNode["join"] = JsonFunction.Create((arguments, _) => JsonELimits.Join(arguments, sizeBudget));
        contextNode["split"] = JsonFunction.Create((arguments, _) => JsonELimits.Split(arguments, sizeBudget));
        contextNode["len"] = JsonFunction.Create((arguments, _) => JsonELimits.Len(arguments, sizeBudget));
        contextNode[JsonEExpressionRewriter.IndexFunction] = JsonFunction.Create(
            (arguments, _) => JsonELimits.Index(arguments, sizeBudget));
        contextNode[JsonEExpressionRewriter.SliceFunction] = JsonFunction.Create(
            (arguments, _) => JsonELimits.Slice(arguments, sizeBudget));
        contextNode["lowercase"] = JsonFunction.Create((arguments, _) => JsonELimits.Lowercase(arguments, sizeBudget));
        contextNode["uppercase"] = JsonFunction.Create((arguments, _) => JsonELimits.Uppercase(arguments, sizeBudget));
        contextNode["strip"] = JsonFunction.Create((arguments, _) => JsonELimits.Strip(arguments, sizeBudget));
        contextNode["lstrip"] = JsonFunction.Create((arguments, _) => JsonELimits.LStrip(arguments, sizeBudget));
        contextNode["rstrip"] = JsonFunction.Create((arguments, _) => JsonELimits.RStrip(arguments, sizeBudget));
        contextNode[JsonEExpressionRewriter.AddFunction] = JsonFunction.Create(JsonELimits.Add);
        contextNode[JsonEExpressionRewriter.InFunction] = JsonFunction.Create(
            (arguments, _) => JsonELimits.In(arguments, sizeBudget));
        contextNode[JsonEExpressionRewriter.EqualsFunction] = JsonFunction.Create(
            (arguments, _) => JsonELimits.Equals(arguments, sizeBudget));
        contextNode[JsonEExpressionRewriter.ConcatFunction] = JsonFunction.Create(JsonELimits.Concat);
        contextNode[JsonEExpressionRewriter.BoundFunction] = JsonFunction.Create(
            (arguments, _) => JsonELimits.Bound(arguments, sizeBudget));
        contextNode[JsonEExpressionRewriter.StepFunction] = JsonFunction.Create(
            (arguments, _) => JsonELimits.Step(arguments, sizeBudget));
        var result = JsonE.Evaluate(definitionNode, contextNode);
        EnsureResultFits(result);
        return JsonTokenConverter.ToToken(result);
    }

    private static void EnsureResultFits(JsonNode? result)
    {
        if (EvaluationSize.SerializedLength(result) > ConfigurationBindingResolver.MaxConcatLength)
        {
            throw new BindingEvaluationException(
                $"Object binding result exceeds {ConfigurationBindingResolver.MaxConcatLength} characters.");
        }
    }
}
