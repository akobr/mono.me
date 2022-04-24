using System;

namespace _42.Functional.Monads
{
    public static class TupleExtensions
    {
        public static Tuple<TFirstResult, TSecond> SelectFirst<TFirst, TFirstResult, TSecond>(
            this Tuple<TFirst, TSecond> tuple,
            Func<TFirst, TFirstResult> selector)
        {
            return Tuple.Create(selector(tuple.Item1), tuple.Item2);
        }

        public static Tuple<TFirst, TSecondResult> Select<TFirst, TSecond, TSecondResult>(
            this Tuple<TFirst, TSecond> tuple,
            Func<TSecond, TSecondResult> selector)
        {
            return Tuple.Create(tuple.Item1, selector(tuple.Item2));
        }

        public static Tuple<TFirstResult, TSecondResult> SelectBoth<TFirst, TFirstResult, TSecond, TSecondResult>(
            this Tuple<TFirst, TSecond> tuple,
            Func<TFirst, TFirstResult> firstSelector,
            Func<TSecond, TSecondResult> secondSelector)
        {
            return tuple.SelectFirst(firstSelector).Select(secondSelector);
        }

#if MATH_NAMING
        public static Tuple<TFirst, TSecondResult> SelectSecond<TFirst, TSecond, TSecondResult>(
            this Tuple<TFirst, TSecond> tuple,
            Func<TSecond, TSecondResult> selector)
        {
            return tuple.Select(selector);
        }

        public static Tuple<TResult, TSecond> First<TFirst, TSecond, TResult>(
            this Tuple<TFirst, TSecond> tuple,
            Func<TFirst, TResult> selector)
        {
            return tuple.SelectFirst(selector);
        }

        public static Tuple<TFirst, TResult> Second<TFirst, TSecond, TResult>(
            this Tuple<TFirst, TSecond> tuple,
            Func<TSecond, TResult> selector)
        {
            return tuple.Select(selector);
        }

        public static Tuple<TFirstResult, TSecondResult> Bimap<TFirst, TFirstResult, TSecond, TSecondResult>(
            Tuple<TFirst, TSecond> tuple,
            Func<TFirst, TFirstResult> firstSelector,
            Func<TSecond, TSecondResult> secondSelector)
        {
            return tuple.SelectBoth(firstSelector, secondSelector);
        }
#endif
    }
}
