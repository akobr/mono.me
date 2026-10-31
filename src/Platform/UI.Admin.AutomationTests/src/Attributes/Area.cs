// In the root namespace like AreaAttribute, so a test writes [Area(Area.Annotations)] without a using.
namespace _42.Platform.UI.Admin.AutomationTests;

// The functional areas of the admin UI; [Area(Area.KeyValues)] is the category "Area-KeyValues".
public enum Area
{
    Access,
    Annotations,
    Configurations,
    KeyValues,
    Modeling,
    Navigation,
    Projects,
    Settings,
}
