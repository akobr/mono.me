using System;

// Storyteller patch: worst-case linear substring search. Not part of upstream JsonE.Net. See VENDORED.md.

namespace Json.JsonE;

/// <summary>
/// Ordinal substring search whose worst case is linear in the input.
/// </summary>
/// <remarks>
/// <see cref="string.Contains(string)"/> filters candidate positions on two characters of the needle and then compares,
/// so a periodic haystack and needle (<c>abab…</c> in <c>abab…aa</c>) cost |haystack| × |needle| comparisons in one call
/// without allocating. Long needles use Knuth–Morris–Pratt instead: at most 2 × |haystack| + 2 × |needle| comparisons.
/// The answer is the same ordinal (UTF-16 code unit) containment.
/// </remarks>
public static class OrdinalSearch
{
	/// <summary>
	/// Needles up to this length keep <see cref="string.Contains(string)"/>; its worst case is then at most 64 × |haystack|.
	/// </summary>
	public const int ShortNeedleLength = 64;

	/// <summary>
	/// Determines whether <paramref name="value"/> occurs in <paramref name="source"/>, comparing UTF-16 code units.
	/// </summary>
	public static bool Contains(string source, string value)
	{
		if (value.Length <= ShortNeedleLength || value.Length > source.Length)
			return source.Contains(value, StringComparison.Ordinal);

		var failure = new int[value.Length];
		for (int i = 1, k = 0; i < value.Length; i++)
		{
			while (k > 0 && value[i] != value[k])
				k = failure[k - 1];
			if (value[i] == value[k])
				k++;
			failure[i] = k;
		}

		for (int i = 0, k = 0; i < source.Length; i++)
		{
			while (k > 0 && source[i] != value[k])
				k = failure[k - 1];
			if (source[i] == value[k])
				k++;
			if (k == value.Length)
				return true;
		}

		return false;
	}
}
