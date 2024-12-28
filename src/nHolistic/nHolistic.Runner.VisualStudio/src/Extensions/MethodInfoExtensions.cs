using System.Reflection;

namespace _42.tHolistic.Runner.VisualStudio;

public static class MethodInfoExtensions
{
    public static bool TryGetOrder(this MethodInfo @this, out int order)
    {
        var orderAttribute = @this.GetCustomAttribute<OrderAttribute>();

        if (orderAttribute is not null)
        {
            order = orderAttribute.Order;
            return true;
        }

        int startIndex = -1, endIndex = -1;

        if (@this.Name.StartsWith('_'))
        {
            startIndex = endIndex = 1;

            while (char.IsAsciiDigit(@this.Name[endIndex]))
            {
                ++endIndex;
            }
        }
        else if (char.IsAsciiDigit(@this.Name[^1]))
        {
            startIndex = @this.Name.Length - 1;
            endIndex = @this.Name.Length;

            while (char.IsAsciiDigit(@this.Name[startIndex - 1]))
            {
                --startIndex;
            }
        }

        order = int.MaxValue;
        var result = startIndex != -1 && endIndex != -1 && int.TryParse(@this.Name[startIndex..endIndex], out order);
        return result;
    }
}
