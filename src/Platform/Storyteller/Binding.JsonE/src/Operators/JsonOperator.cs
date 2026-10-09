using System;
using System.Collections.Generic;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Nodes;
using Json.More;

namespace Json.JsonE.Operators;

internal class JsonOperator : IOperator
{
	private static readonly JsonSerializerOptions _serializerOptions =
		new()
		{
			TypeInfoResolverChain = { JsonESerializerContext.Default },
			Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping
		};

	public const string Name = "$json";

	public JsonNode? Evaluate(JsonNode? template, EvaluationContext context)
	{
		var obj = template!.AsObject();
		obj.VerifyNoUndefinedProperties(Name);
	
		var value = obj[Name];

		var evaluated = Sort(JsonE.Evaluate(value, context));
		evaluated.ValidateNotReturningFunction();

		Metering.ReserveChars(SerializedLengthUpperBound(evaluated));
		return evaluated.AsJsonString(_serializerOptions);
	}

	// Storyteller patch: the value may hold many references to one string instance, so the serialized size is
	// reserved before the string is built. Strings count 6 characters per character (worst-case escape). The walk
	// visits nodes, which are already allocated, and never scans string contents. See VENDORED.md.
	private static long SerializedLengthUpperBound(JsonNode? root)
	{
		long length = 0;
		var pending = new Stack<JsonNode?>();
		pending.Push(root);
		while (pending.Count > 0)
		{
			switch (pending.Pop())
			{
				case null:
					length += 4;
					break;
				case JsonObject obj:
					length += 2;
					foreach (var kvp in obj)
					{
						length += (kvp.Key.Length * 6L) + 4;
						pending.Push(kvp.Value);
					}
					break;
				case JsonArray arr:
					length += 2;
					foreach (var item in arr)
					{
						length += 1;
						pending.Push(item);
					}
					break;
				case JsonValue val when val.TryGetValue(out string? str):
					length += (str.Length * 6L) + 2;
					break;
				default:
					length += 32;
					break;
			}
		}

		return length;
	}

	private static JsonNode? Sort(JsonNode? node)
	{
		if (node is not JsonObject obj) return node;

		var dict = new SortedDictionary<string, JsonNode?>(StringComparer.Ordinal);
		foreach (var kvp in obj)
		{
			dict[kvp.Key] = Sort(kvp.Value);
		}
		
		return JsonSerializer.SerializeToNode(dict!, JsonESerializerContext.Default.SortedDictionaryStringJsonNode);
	}
}