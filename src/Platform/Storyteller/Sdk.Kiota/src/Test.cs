using System.Threading.Tasks;

namespace ApiSdk;

public class Test
{
    public static async Task Foo(ApiClient client)
    {
        var allDescendants = await client.V1["org"]["project"]["view"].Annotations["key"]["all"].GetAsync();
        var config = await client.V1["org"]["project"]["view"].Configuration["key"].Resolved.GetAsync();
    }
}
