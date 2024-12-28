namespace _42.tHolistic;

public interface IFixture
{
    // no member
}

public interface IFixture<TFixture> : IFixture
    where TFixture : class
{
    // no member
}
