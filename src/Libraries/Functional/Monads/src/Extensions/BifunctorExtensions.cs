using System;

namespace _42.Functional.Monads
{
    public static class BifunctorExtensions
    {
        public static IBifunctor<TFirst, TResult> SelectSecond<TFirst, TSecond, TResult>(
            IBifunctor<TFirst, TSecond> bifunctor,
            Func<TSecond, TResult> selector)
        {
            return bifunctor.Select(selector);
        }

#if MATH_NAMING
        public static IBifunctor<TResult, TSecond> First<TFirst, TSecond, TResult>(
            IBifunctor<TFirst, TSecond> bifunctor,
            Func<TFirst, TResult> selector)
        {
            return bifunctor.SelectFirst(selector);
        }

        public static IBifunctor<TFirst, TResult> Second<TFirst, TSecond, TResult>(
            IBifunctor<TFirst, TSecond> bifunctor,
            Func<TSecond, TResult> selector)
        {
            return bifunctor.Select(selector);
        }

        public static IBifunctor<TFirstResult, TSecondResult> Bimap<TFirst, TFirstResult, TSecond, TSecondResult>(
            IBifunctor<TFirst, TSecond> bifunctor,
            Func<TFirst, TFirstResult> firstSelector,
            Func<TSecond, TSecondResult> secondSelector)
        {
            return bifunctor.SelectBoth(firstSelector, secondSelector);
        }
#endif
    }
}
