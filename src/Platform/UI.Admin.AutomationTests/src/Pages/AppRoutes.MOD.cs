using System.Text.RegularExpressions;

namespace _42.Platform.UI.Admin.AutomationTests.Pages;

// The Modeling section's member of AppRoutes: a link's href by the route it ends with, for the links of the Schemas list
// and of an import's result.
public sealed partial record AppRoutes
{
    // An href that ends with the route (a path of the app, e.g. Routes.AnnotationSchema(key)), wherever the app is served.
    public static Regex HrefEndingWith(string route)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(route);
        return new Regex($"/{Regex.Escape(route.Trim('/'))}$");
    }
}
