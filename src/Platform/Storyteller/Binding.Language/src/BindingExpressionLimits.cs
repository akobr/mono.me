namespace _42.Platform.Storyteller.Binding.Language;

/// <summary>
/// The bounds of one <c>@</c> binding string. They are the bounds of the MCP server's reader
/// (<c>Mcp/src/Understanding/BindingExpressions.cs</c>), so both agree on which strings are too long or too deep.
/// </summary>
public static class BindingExpressionLimits
{
    /// <summary>
    /// The length of the whole string, the leading '@' included, in UTF-16 code units.
    /// </summary>
    public const int MaxLength = 8 * 1024;

    /// <summary>
    /// Nested statements and math parentheses: every statement is a level (the outermost, a statement argument,
    /// an interpolation part, a math operand, and the innermost <c>@x</c>), and so is every parenthesis of math.
    /// The <c>@[</c> and <c>@{</c> wrappers, the parentheses of a call, and sibling statements are not.
    /// </summary>
    public const int MaxDepth = 64;
}
