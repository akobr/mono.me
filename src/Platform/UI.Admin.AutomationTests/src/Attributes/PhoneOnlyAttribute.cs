using TUnit.Core;

// In the root namespace like AreaAttribute, so a test writes [PhoneOnly] without a using.
namespace _42.Platform.UI.Admin.AutomationTests;

// A test of the phone layout (below the sm breakpoint): the category PhoneOnly, which PlatformTest.GetSkipReason skips
// before the lease when the session's viewport is App.PhoneWidth (640 px) or wider.
public sealed class PhoneOnlyAttribute() : CategoryAttribute(PlatformCategories.PhoneOnly);
