using System;

namespace _42.Functional.Monads
{
    public interface IBifunctor<out TFirst, out TSecond> : IFunctor<TSecond>
    {
        IBifunctor<TResult, TSecond> SelectFirst<TResult>(Func<TFirst, TResult> selector);

        new IBifunctor<TFirst, TResult> Select<TResult>(Func<TSecond, TResult> selector);

        IBifunctor<TFirstResult, TSecondResult> SelectBoth<TFirstResult, TSecondResult>(
            Func<TFirst, TFirstResult> firstSelector,
            Func<TSecond, TSecondResult> secondSelector);
    }
}