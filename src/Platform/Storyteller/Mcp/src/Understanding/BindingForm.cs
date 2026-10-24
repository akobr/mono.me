namespace _42.Platform.Storyteller.Mcp.Understanding;

// The top-level form of a binding (docs/Platform/Storyteller/binding.md).
public enum BindingForm
{
    // @path.to.value: the default source, the Values store
    Path,

    // @(path, source)
    Sourced,

    // @source(path): @public(...), @secret(...) or another registered source
    SourceCall,

    // @config("<pointer>"): this document
    Config,

    // @annotation("<pointer>"[, "<type>"]): the annotation's values
    Annotation,

    // @name(arguments) of another function
    Function,

    // @[text @statement text]
    Interpolation,

    // @{ expression }
    Math,

    // an object with a string $binding: jlogic or jsone
    Object,

    // a string that starts with '@' but does not parse
    Invalid,
}
