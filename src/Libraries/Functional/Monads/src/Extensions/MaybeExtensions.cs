using System;

namespace _42.Functional.Monads
{
    public static class MaybeExtensions
    {
#if MATH_NAMING
        public static Maybe<TResult> Map<TValue, TResult>(
            this Maybe<TValue> maybe,
            Func<TValue, TResult> selector)
        {
            return maybe.Select(selector);
        }
#endif
    }
}
