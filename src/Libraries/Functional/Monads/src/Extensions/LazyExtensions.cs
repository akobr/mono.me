using System;

namespace _42.Functional.Monads
{
    public static class LazyExtensions
    {
        public static Lazy<TResult> Select<TValue, TResult>(
            this Lazy<TValue> lazy,
            Func<TValue, TResult> selector)
        {
            return new Lazy<TResult>(() => selector(lazy.Value));
        }

#if MATH_NAMING
        public static Lazy<TResult> Map<TValue, TResult>(
            this Lazy<TValue> lazy,
            Func<TValue, TResult> selector)
        {
            return Select(lazy, selector);
        }
#endif
    }
}
