using System.Text;
using Bogus;

namespace _42.Testing.UI.Automation.Runtime.Data;

// The generated data of one attempt of one test. The seed combines the session seed, the stable test id and the
// attempt, so a test gets the same data for a seed whatever the order or the process, and a retry gets fresh names.
// One Randomizer and one Faker per test; never Bogus' global Randomizer.Seed. Domain generators (keys, documents) build
// on this in the consuming project.
public sealed class SeededData
{
    private const string Letters = "abcdefghijklmnopqrstuvwxyz";

    public SeededData(int sessionSeed, string testId, int attempt = 1, string locale = "en")
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(testId);

        SessionSeed = sessionSeed;
        TestId = testId;
        Attempt = attempt;
        Seed = StableHash.Combine(sessionSeed, testId, attempt);
        Random = new Randomizer(Seed);
        Faker = new Faker(locale) { Random = Random };
    }

    public int SessionSeed { get; }

    public string TestId { get; }

    public int Attempt { get; }

    // The seed of this test's Random and Faker.
    public int Seed { get; }

    public Randomizer Random { get; }

    public Faker Faker { get; }

    // A random session seed, logged by the session so the run can be repeated with Execution:Seed.
    public static int NewSessionSeed()
    {
        return System.Random.Shared.Next();
    }

    // Lower-case ASCII letters, at least two.
    public string Word()
    {
        for (var attempt = 0; attempt < 10; attempt++)
        {
            var word = Normalize(Faker.Lorem.Word(), allowDash: false);
            if (word.Length >= 2)
            {
                return word;
            }
        }

        return Random.String2(6, Letters);
    }

    // "<prefix>-<word>-<word>" of lower-case letters, digits and '-'; the prefix is normalized to the same charset.
    public string Slug(string prefix, int words = 2)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(prefix);
        ArgumentOutOfRangeException.ThrowIfNegative(words);

        var parts = new List<string> { Normalize(prefix, allowDash: true) };
        for (var index = 0; index < words; index++)
        {
            parts.Add(Word());
        }

        return string.Join('-', parts.Where(part => part.Length > 0));
    }

    // A sentence of at most maxLength characters, cut at a word.
    public string Sentence(int maxLength = 80)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(maxLength, 1);

        var sentence = Faker.Lorem.Sentence();
        if (sentence.Length <= maxLength)
        {
            return sentence;
        }

        var cut = sentence.LastIndexOf(' ', maxLength - 1, maxLength);
        return (cut > 0 ? sentence[..cut] : sentence[..maxLength]).TrimEnd();
    }

    // Lower-case hex digits.
    public string Hex(int length = 8)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(length, 1);
        return Random.Hexadecimal(length, prefix: string.Empty).ToLowerInvariant();
    }

    private static string Normalize(string value, bool allowDash)
    {
        var builder = new StringBuilder(value.Length);
        foreach (var character in value.ToLowerInvariant())
        {
            if (character is (>= 'a' and <= 'z') or (>= '0' and <= '9'))
            {
                builder.Append(character);
            }
            else if (allowDash && builder.Length > 0 && builder[^1] != '-')
            {
                builder.Append('-');
            }
        }

        return builder.ToString().TrimEnd('-');
    }
}
