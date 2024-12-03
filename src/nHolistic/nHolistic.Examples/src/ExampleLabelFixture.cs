namespace _42.nHolistic.Examples;

[Label("CustomFixture")]
public class ExampleLabelFixture : IFixture<ExampleLabelFixture>, IAsyncLifetime
{
    public ValueTask DisposeAsync()
    {
        throw new NotImplementedException();
    }

    public ValueTask InitializeAsync()
    {
        throw new NotImplementedException();
    }
}
