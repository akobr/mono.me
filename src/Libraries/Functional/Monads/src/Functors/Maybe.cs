using System;

namespace _42.Functional.Monads
{
    public class Maybe<TValue> : IFunctor<TValue>
    {
        private readonly bool _hasValue;
        private readonly TValue _value;

        public Maybe()
        {
            // no operation
        }

        public Maybe(TValue value)
        {
            if (value == null)
            {
                throw new ArgumentNullException(nameof(value));
            }

            _hasValue = true;
            this._value = value;
        }

        public TResult Match<TResult>(TResult nothing, Func<TValue, TResult> just)
        {
            if (nothing == null)
            {
                throw new ArgumentNullException(nameof(nothing));
            }

            if (just == null)
            {
                throw new ArgumentNullException(nameof(just));
            }

            return _hasValue ? just(_value) : nothing;
        }

        public Maybe<TResult> Select<TResult>(Func<TValue, TResult> selector)
        {
            if (selector == null)
            {
                throw new ArgumentNullException(nameof(selector));
            }

            return _hasValue
                ? new Maybe<TResult>(selector(_value))
                : new Maybe<TResult>();
        }

        IFunctor<TResult> IFunctor<TValue>.Select<TResult>(Func<TValue, TResult> selector)
        {
            return Select(selector);
        }

        public Maybe<TResult> SelectMany<TResult>(Func<TValue, Maybe<TResult>> selector)
        {
            if (selector == null)
            {
                throw new ArgumentNullException(nameof(selector));
            }

            return _hasValue
                ? selector(_value)
                : new Maybe<TResult>();
        }

        public TValue GetValue(TValue fallbackValue)
        {
            if (fallbackValue == null)
            {
                throw new ArgumentNullException(nameof(fallbackValue));
            }

            return _hasValue ? _value : fallbackValue;
        }

        public TValue GetValue(Func<TValue> fallback)
        {
            if (fallback == null)
            {
                throw new ArgumentNullException(nameof(fallback));
            }

            return _hasValue ? _value : fallback();
        }

        public override bool Equals(object obj)
        {
            if (obj is Maybe<TValue> other)
            {
                return Equals(_value, other._value);
            }

            return false;
        }

        public override int GetHashCode()
        {
            return _hasValue ? _value.GetHashCode() : 0;
        }

        public override string ToString()
        {
            return _hasValue ? _value.ToString() : Constants.NULL;
        }
    }
}
