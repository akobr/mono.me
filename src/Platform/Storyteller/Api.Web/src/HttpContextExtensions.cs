namespace _42.Platform.Storyteller.Api;

public static class HttpContextExtensions
{
    public static string GetTenant(this HttpContext @this)
    {
        var principal = @this.User;
        return Constants.DefaultTenantName;
    }
}
