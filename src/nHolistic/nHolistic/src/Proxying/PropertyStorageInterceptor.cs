using Castle.DynamicProxy;

namespace _42.tHolistic;

public class PropertyStorageInterceptor : IInterceptor
{
    private readonly Dictionary<string, object> _propertyValues = new();

    public void Intercept(IInvocation invocation)
    {
        if (!invocation.Method.IsSpecialName
            || invocation.Method.Name.Length < 5)
        {
            return;
        }

        var propertyName = invocation.Method.Name[4..];

        if (invocation.Method.Name.StartsWith("get_"))
        {
            if (_propertyValues.TryGetValue(propertyName, out var value))
            {
                invocation.ReturnValue = value;
            }
        }
        else if (invocation.Method.Name.StartsWith("set_"))
        {
            _propertyValues[propertyName] = invocation.Arguments[0];
        }
    }
}
