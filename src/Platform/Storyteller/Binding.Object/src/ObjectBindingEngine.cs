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
    }

    public static void UseLogger(ILogger? logger)
    {
        JsonLogicDebugLogger.Instance.Use(logger);
    }

    public static JToken Evaluate(string kind, JToken definition, JObject context)
    {
        try
        {
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
                JsonEKind => JsonE.Evaluate(definitionNode, contextNode),
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
}
