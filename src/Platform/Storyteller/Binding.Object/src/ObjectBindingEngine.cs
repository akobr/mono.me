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

    public static JToken Evaluate(string kind, JToken definition, JObject context)
    {
        try
        {
            if (kind == JsonEKind)
            {
                return EvaluateJsonE(definition, context);
            }

            var definitionNode = JsonTokenConverter.ToNode(definition);
            if (definitionNode is null)
            {
                return JValue.CreateNull();
            }

            var contextNode = JsonTokenConverter.ToNode(context)
                ?? throw new BindingEvaluationException("Object binding $context must be a JSON object.");

            JsonNode? result = kind switch
            {
                JsonLogicKind => JsonLogic.Apply(definitionNode, contextNode),
                _ => throw new BindingEvaluationException($"Unknown object binding '{kind}'."),
            };

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

    private static JToken EvaluateJsonE(JToken definition, JObject context)
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
        contextNode["range"] = JsonFunction.Create(JsonELimits.Range);
        contextNode[JsonEExpressionRewriter.AddFunction] = JsonFunction.Create(JsonELimits.Add);
        contextNode[JsonEExpressionRewriter.ConcatFunction] = JsonFunction.Create(JsonELimits.Concat);
        return JsonTokenConverter.ToToken(JsonE.Evaluate(definitionNode, contextNode));
    }
}
