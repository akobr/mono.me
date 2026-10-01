using Microsoft.Azure.Functions.Worker.Http;

namespace _42.Platform.Storyteller.Api.Functions.UnitTests;

internal sealed class TestHttpCookies : HttpCookies
{
    public override void Append(string name, string value)
    {
    }

    public override void Append(IHttpCookie cookie)
    {
    }

    public override IHttpCookie CreateNew()
    {
        throw new NotSupportedException();
    }
}
