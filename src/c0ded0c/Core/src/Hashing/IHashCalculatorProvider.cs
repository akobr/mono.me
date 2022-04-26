namespace c0ded0c.Core.Hashing
{
    public interface IHashCalculatorProvider
    {
        IHashCalculator GetCalculator();
    }
}
