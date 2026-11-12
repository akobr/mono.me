using System.Collections.Immutable;

using Microsoft.Azure.Functions.Worker;

namespace _42.Platform.Storyteller.Api.Functions.UnitTests;

// A function by its name, as the worker describes it.
internal sealed class NamedFunctionDefinition(string name) : FunctionDefinition
{
    public override ImmutableArray<FunctionParameter> Parameters => [];

    public override string PathToAssembly => string.Empty;

    public override string EntryPoint => string.Empty;

    public override string Id => name;

    public override string Name => name;

    public override IImmutableDictionary<string, BindingMetadata> InputBindings => ImmutableDictionary<string, BindingMetadata>.Empty;

    public override IImmutableDictionary<string, BindingMetadata> OutputBindings => ImmutableDictionary<string, BindingMetadata>.Empty;
}
