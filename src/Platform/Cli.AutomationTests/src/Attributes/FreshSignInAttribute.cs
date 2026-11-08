using TUnit.Core;

// In the root namespace like AreaAttribute, so a test writes [FreshSignIn] without a using.
namespace _42.Platform.Cli.AutomationTests;

// A test of the sign-in itself (mode F): the category FreshSignIn. Its runs use an empty SFORM_HOME of its own instead of
// the leased user's signed-in one (CliTest.Home), so it starts signed out and signs in itself (CliTest.SignInAsync, or
// StartSform of sform account and the device page); it still leases a pool user, whose credentials approve the code.
public sealed class FreshSignInAttribute() : CategoryAttribute(CliCategories.FreshSignIn);
