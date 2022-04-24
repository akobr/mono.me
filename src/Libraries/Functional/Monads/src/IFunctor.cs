using System;

namespace _42.Functional.Monads
{
    public interface IFunctor<out TContext>
    {
        IFunctor<TResult> Select<TResult>(Func<TContext, TResult> selector);
    }
}