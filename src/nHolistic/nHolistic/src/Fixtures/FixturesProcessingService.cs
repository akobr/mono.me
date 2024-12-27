using System.Reflection;

namespace _42.nHolistic;

public class FixturesProcessingService(
    IFixtureStorage storage,
    ISynchronizationService synchronization) : IFixturesProcessingService
{
    private readonly HashSet<string> _executedAssemblies = new(StringComparer.OrdinalIgnoreCase);

    public void PrepareFixtures(Type testClassType)
    {
        var assembly = testClassType.Assembly;
        var key = assembly.GetName().FullName;

        lock (_executedAssemblies)
        {
            if (!_executedAssemblies.Add(key))
            {
                return;
            }
        }

        var lockKey = $"Fixtures:{key}";
        lock (synchronization.GetOrCreateLock(lockKey))
        {
            RegisterFixtures(assembly);
        }
    }

    private void RegisterFixtures(Assembly assembly)
    {
        var fixtureDefinitionTypes = assembly.GetTypes()
            .Where(type => type.IsFixture());

        foreach (var type in fixtureDefinitionTypes)
        {
            var fixtureLabel = type.GetFixtureTargetLabel();
            var fixtureType = type.GetFixtureType();
            storage.RegisterFixture(fixtureLabel, fixtureType);
        }
    }
}
