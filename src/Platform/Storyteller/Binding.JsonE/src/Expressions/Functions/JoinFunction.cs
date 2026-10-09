using System.Linq;
using System.Text.Json.Nodes;
using Json.JsonE.Operators;
using Json.More;

namespace Json.JsonE.Expressions.Functions;

internal class JoinFunction : FunctionDefinition
{
	private const string _name = "join";

	internal override JsonNode? Invoke(JsonNode?[] arguments, EvaluationContext context)
	{
		if (arguments[0] is not JsonArray arr)
			throw new BuiltInException(CommonErrors.IncorrectArgType(_name));

		if (arguments[1] is not JsonValue v)
			throw new BuiltInException(CommonErrors.IncorrectArgType(_name));
		var separator = v.GetNumber()?.ToString();
		if (separator == null && !v.TryGetValue(out separator))
			throw new BuiltInException(CommonErrors.IncorrectArgType(_name));

		long joinedLength = 0;
		var parts = arr.Select(x =>
		{
			if (x is not JsonValue val)
				throw new BuiltInException(CommonErrors.IncorrectArgType(_name));

			var num = val.GetNumber();
			if (num.HasValue) return Reserved(num.ToString());

			if (val.TryGetValue(out string? str)) return Reserved(str);

			if (val.TryGetValue(out bool b)) return Reserved(b.ToString().ToLowerInvariant());

			throw new BuiltInException(CommonErrors.IncorrectArgType(_name));
		}).ToList();

		return string.Join(separator, parts);

		// Storyteller patch: array items may share one string instance, so the joined length is reserved per part,
		// before string.Join builds the result. See VENDORED.md.
		string? Reserved(string? part)
		{
			joinedLength += (part?.Length ?? 0) + separator!.Length;
			Metering.ReserveChars(joinedLength);
			return part;
		}
	}
}