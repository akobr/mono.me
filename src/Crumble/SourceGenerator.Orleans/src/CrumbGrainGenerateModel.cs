using Microsoft.CodeAnalysis;

namespace _42.Crumble.SourceGenerator;

public record class CrumbGrainGenerateModel
{
    public string CrumbKey { get; set; }

    public string CrumbName { get; set; }

    public INamedTypeSymbol Class { get; set; }

    public IMethodSymbol Method { get; set; }

    public INamespaceSymbol Namespace { get; set; }

    public string NamespaceFullName { get; set; }

    public bool IsSingleAndSynchronized { get; set; }

    public int ParameterCount { get; set; }

    public bool HasInput { get; set; }

    public bool HasOutput { get; set; }

    public bool IsAsync { get; set; }

    public string InputTypeName { get; set; }

    public string OutputTypeName { get; set; }
}
