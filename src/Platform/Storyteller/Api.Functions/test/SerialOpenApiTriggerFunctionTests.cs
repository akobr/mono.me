using _42.Platform.Storyteller.Api.OpenApi;

using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Extensions.OpenApi;
using Microsoft.Azure.Functions.Worker.Http;
using Microsoft.Extensions.DependencyInjection;

namespace _42.Platform.Storyteller.Api.Functions.UnitTests;

public class SerialOpenApiTriggerFunctionTests
{
    [Fact]
    public async Task OverlappingRenders_RunOneAtATime()
    {
        var inner = new TrackingTrigger();
        var function = new SerialOpenApiTriggerFunction(inner);
        var context = new TestFunctionContext(new ServiceCollection().BuildServiceProvider());

        await Task.WhenAll(
            Enumerable.Range(0, 8).Select(_ => function.RenderSwaggerDocument(null!, "json", context)));

        inner.MaxActive.ShouldBe(1);
        inner.Calls.ShouldBe(8);
    }

    [Fact]
    public async Task CanceledRequest_DoesNotWaitForTheGate()
    {
        var inner = new TrackingTrigger();
        var function = new SerialOpenApiTriggerFunction(inner);
        using var canceled = new CancellationTokenSource();
        await canceled.CancelAsync();
        var context = new TestFunctionContext(new ServiceCollection().BuildServiceProvider())
        {
            Token = canceled.Token,
        };

        await Should.ThrowAsync<OperationCanceledException>(() => function.RenderSwaggerDocument(null!, "json", context));

        inner.Calls.ShouldBe(0);
    }

    private sealed class TrackingTrigger : IOpenApiTriggerFunction
    {
        private int _active;
        private int _maxActive;

        public int Calls { get; private set; }

        public int MaxActive => _maxActive;

        public Task<HttpResponseData> RenderSwaggerDocument(HttpRequestData req, string extension, FunctionContext ctx)
        {
            return Track();
        }

        public Task<HttpResponseData> RenderOpenApiDocument(HttpRequestData req, string version, string extension, FunctionContext ctx)
        {
            return Track();
        }

        public Task<HttpResponseData> RenderSwaggerUI(HttpRequestData req, FunctionContext ctx)
        {
            return Track();
        }

        public Task<HttpResponseData> RenderOAuth2Redirect(HttpRequestData req, FunctionContext ctx)
        {
            return Track();
        }

        private async Task<HttpResponseData> Track()
        {
            var active = Interlocked.Increment(ref _active);
            try
            {
                int observed;
                do
                {
                    observed = _maxActive;
                    if (active <= observed)
                    {
                        break;
                    }
                }
                while (Interlocked.CompareExchange(ref _maxActive, active, observed) != observed);

                await Task.Delay(30);
                Calls++;
                return null!;
            }
            finally
            {
                Interlocked.Decrement(ref _active);
            }
        }
    }
}
