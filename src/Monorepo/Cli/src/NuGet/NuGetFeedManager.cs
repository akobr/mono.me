using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading.Tasks;
using Semver;

namespace _42.Monorepo.Cli.NuGet
{
    // TODO: [P2] add support for other feeds based on nuget.config
    // https://stackoverflow.com/questions/49789283/read-nuget-config-programmatically-in-c-sharp
    public class NuGetFeedManager
    {
        private readonly string _nugetFeedUrl;
        private readonly bool _usePrereleases;

        public NuGetFeedManager(bool usePrereleases)
            : this("https://api.nuget.org", usePrereleases)
        {
            // no operation
        }

        public NuGetFeedManager(string nugetFeedUrl, bool usePrereleases)
        {
            _nugetFeedUrl = nugetFeedUrl;
            _usePrereleases = usePrereleases;
        }

        public async Task<IReadOnlySet<SemVersion>> GetPossibleVersionsAsync(string packageId)
        {
            var client = new HttpClient();
            var versionsResponse = await client.GetAsync($"{_nugetFeedUrl}/v3-flatcontainer/{packageId}/index.json");

            if (!versionsResponse.IsSuccessStatusCode)
            {
                return new HashSet<SemVersion>();
            }

            using var contentStream = await versionsResponse.Content.ReadAsStreamAsync();
            var versionInfo = await JsonSerializer.DeserializeAsync<VersionInfo>(contentStream);

            if (versionInfo is null)
            {
                return new HashSet<SemVersion>();
            }

            var versions = _usePrereleases
                ? versionInfo.Versions.Select(v => SemVersion.Parse(v))
                : versionInfo.Versions.Select(v => SemVersion.Parse(v)).Where(v => string.IsNullOrEmpty(v.Prerelease));

            return new SortedSet<SemVersion>(versions, new LatestFirstVersionComparer());
        }

        private class VersionInfo
        {
            [JsonPropertyName("versions")]
            public List<string> Versions { get; set; } = new List<string>();
        }

        private class LatestFirstVersionComparer : IComparer<SemVersion>
        {
            public int Compare(SemVersion? x, SemVersion? y)
            {
                return SemVersion.Compare(y, x);
            }
        }
    }
}
