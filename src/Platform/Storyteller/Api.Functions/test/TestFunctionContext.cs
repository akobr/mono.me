using Microsoft.Azure.Functions.Worker;

namespace _42.Platform.Storyteller.Api.Functions.UnitTests;

internal sealed class TestFunctionContext : FunctionContext
{
    public TestFunctionContext(IServiceProvider services)
    {
        Items = new Dictionary<object, object>();
        Features = new TestInvocationFeatures();
        InstanceServices = services;
    }

    public override string InvocationId { get; } = "invocation";

    public override string FunctionId { get; } = "function";

    public override TraceContext TraceContext => throw new NotSupportedException();

    public override BindingContext BindingContext => throw new NotSupportedException();

    public override RetryContext RetryContext => throw new NotSupportedException();

    public override IServiceProvider InstanceServices { get; set; }

    public override FunctionDefinition FunctionDefinition => throw new NotSupportedException();

    public override IDictionary<object, object> Items { get; set; }

    public override IInvocationFeatures Features { get; }

    public override CancellationToken CancellationToken => Token;

    public CancellationToken Token { get; set; }
}
