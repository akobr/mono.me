using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;

namespace _42.Platform.Storyteller.Api;

public static class FunctionContextExtensions
{
    public static void SetInvocationResult(this FunctionContext @this, HttpResponseData response)
    {
        var invocationResult = @this.GetInvocationResult();
        var httpOutputBindingFromMultipleOutputBindings = GetHttpOutputBindingFromMultipleOutputBinding(@this);

        if (httpOutputBindingFromMultipleOutputBindings is not null)
        {
            httpOutputBindingFromMultipleOutputBindings.Value = response;
        }
        else
        {
            invocationResult.Value = response;
        }
    }

    private static OutputBindingData<HttpResponseData>? GetHttpOutputBindingFromMultipleOutputBinding(FunctionContext context)
    {
        // The output binding entry name will be "$return" only when the function return type is HttpResponseData
        var httpOutputBinding = context.GetOutputBindings<HttpResponseData>()
            .FirstOrDefault(b => b.BindingType == "http" && b.Name != "$return");

        return httpOutputBinding;
    }
}
