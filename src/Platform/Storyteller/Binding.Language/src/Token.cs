namespace _42.Platform.Storyteller.Binding.Language;

/// <summary>
/// A single lexical token. <see cref="Start"/> and <see cref="Length"/> describe the token's span in
/// the source text (for diagnostics); <see cref="Lexeme"/> holds the decoded value (e.g. interpolation
/// text and string literals are stored without their escape sequences or surrounding quotes).
/// </summary>
public readonly record struct Token(TokenType Type, string Lexeme, int Start, int Length);
