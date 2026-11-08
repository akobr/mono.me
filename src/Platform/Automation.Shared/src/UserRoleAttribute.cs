using System.Collections.Concurrent;
using System.Reflection;

namespace _42.Platform.Automation.Shared;

// The role a test acts as (Role), on the test method or its class; the method's wins. A project's test base class reads
// it (Of) for the pool user it leases (PlatformRoles.RequirementOf) and the project it works in
// (PlatformTargetSettings.RoleProjects). Without it (and without an override of the test's RequiredUser) a test runs
// as any pool user in the target's project.
[AttributeUsage(AttributeTargets.Class | AttributeTargets.Method, Inherited = true, AllowMultiple = false)]
public sealed class UserRoleAttribute(Role role) : Attribute
{
    // By class and method; reflection, because a test engine's metadata (TUnit's) keeps only attributes of its own types.
    private static readonly ConcurrentDictionary<(Type Type, string Method), Role> Roles = new();

    public Role Role { get; } = role;

    // The role of a test: [UserRole] of the method (any overload of the name), else of its class or a base class; Role.Any
    // without one. method: null when only the class is known (not kept).
    public static Role Of(Type testClass, string? method)
    {
        ArgumentNullException.ThrowIfNull(testClass);

        return method is null
            ? testClass.GetCustomAttribute<UserRoleAttribute>(inherit: true)?.Role ?? Role.Any
            : Roles.GetOrAdd((testClass, method), static key =>
            {
                const BindingFlags flags = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static;
                return key.Type.GetMethods(flags)
                    .Where(candidate => string.Equals(candidate.Name, key.Method, StringComparison.Ordinal))
                    .Select(candidate => candidate.GetCustomAttribute<UserRoleAttribute>(inherit: true))
                    .FirstOrDefault(attribute => attribute is not null)?.Role
                    ?? key.Type.GetCustomAttribute<UserRoleAttribute>(inherit: true)?.Role
                    ?? Role.Any;
            });
    }
}
