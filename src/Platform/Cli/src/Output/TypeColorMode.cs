namespace _42.Platform.Cli.Output;

// How the console output colors the annotation types of a project.
public enum TypeColorMode
{
    // No type colors: no console, NO_COLOR, or a project that changes no type; a tree tag keeps the lowlight color.
    None,

    // The nearest of the 16 console colors (TypeColors).
    Console,

    // The admin UI's 24-bit shade where sform writes the escape codes itself (the tree tags); tables still use the
    // nearest console color.
    TrueColor,
}
