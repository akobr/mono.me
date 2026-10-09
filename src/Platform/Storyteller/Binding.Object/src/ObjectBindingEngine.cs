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
        JsonLogicMetering.Install();
    }

    public static void UseLogger(ILogger? logger)
    {
        JsonLogicDebugLogger.Instance.Use(logger);
    }

    /// <summary>
    /// Evaluates one envelope. The engine call is metered by the read's <paramref name="meter"/>. The conversions around it
    /// are linear in data that is already bounded (the stored document and the checked result) and are not metered.
    /// Every failure is a <see cref="BindingException"/>; anything thrown by an engine for author-supplied input becomes
    /// <see cref="BindingEvaluationException"/>.
    /// </summary>
    public static JToken Evaluate(string kind, JToken definition, JObject context, EvaluationMeter meter)
    {
        try
        {
            var definitionNode = JsonTokenConverter.ToNode(definition, meter.Limits.MaxDepth);
            if (definitionNode is null)
            {
                return JValue.CreateNull();
            }

            if (JsonTokenConverter.ToNode(context, meter.Limits.MaxDepth) is not JsonObject contextNode)
            {
                throw new BindingEvaluationException("Object binding $context must be a JSON object.");
            }

            JsonNode? result;
            using (meter.Enter())
            {
                result = kind switch
                {
                    JsonLogicKind => JsonLogic.Apply(definitionNode, contextNode),
                    JsonEKind => JsonE.Evaluate(definitionNode, contextNode, meter),
                    _ => throw new BindingEvaluationException($"Unknown object binding '{kind}'."),
                };
            }

            // A library catch block could have swallowed the meter's exception. The limit is latched.
            meter.ThrowIfExceeded();
            ResultCheck.Ensure(result, meter.Limits);
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
        catch (InsufficientExecutionStackException exception)
        {
            throw new EvaluationLimitExceededException(
                EvaluationLimitKind.Depth,
                meter.Limits.MaxDepth,
                $"Object binding evaluation exceeds nesting depth {meter.Limits.MaxDepth} or the available stack.",
                path: null,
                exception);
        }
        catch (Exception exception) when (exception is not OutOfMemoryException)
        {
            // The input is author-supplied, so an engine failure is a client error, not a server fault.
            throw new BindingEvaluationException($"Object binding evaluation failed: {exception.Message}", exception);
        }
    }
}
