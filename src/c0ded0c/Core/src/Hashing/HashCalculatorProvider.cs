namespace c0ded0c.Core.Hashing
{
    public class HashCalculatorProvider : IHashCalculatorProvider
    {
        public IHashCalculator GetCalculator()
        {
            return new MurmurHash3();
        }
    }
}
