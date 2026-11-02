// In the root namespace on purpose: every test namespace is below it, so [UserRole(Role.Reader)] resolves without a
// using.
namespace _42.Platform.UI.Admin.AutomationTests;

// The role a test acts as (Role), on the test method or its class; the method's wins. PlatformTest reads it for
// RequiredUser, Routes and ActingRole. Without it (and without an override of RequiredUser) a test runs as any pool
// user in the target's project.
[AttributeUsage(AttributeTargets.Class | AttributeTargets.Method, Inherited = true, AllowMultiple = false)]
public sealed class UserRoleAttribute(Role role) : Attribute
{
    public Role Role { get; } = role;
}
