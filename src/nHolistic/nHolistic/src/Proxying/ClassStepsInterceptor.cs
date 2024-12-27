using System.Reflection;
using Castle.DynamicProxy;

namespace _42.nHolistic;

public class ClassStepsInterceptor(ITestRunContext context) : IInterceptor
{
    public void Intercept(IInvocation invocation)
    {
        var method = invocation.Method;
        var stepAttribute = method.GetCustomAttribute<StepAttribute>();

        if (stepAttribute is null)
        {
            invocation.Proceed();
            return;
        }

        using (context.StartStep(method.Name, stepAttribute))
        {
            invocation.Proceed();
        }
    }
}
