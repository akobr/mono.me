namespace _42.nHolistic;

public interface IFixture
{
    // no member
}

public interface IFixture<TFixture> : IFixture
    where TFixture : class
{
    // no member
}
