#:package NodaTime
#:property PublishAot=false
#:property EnableStyleCop=false
#:property EnableGitVersioning=false
#:property Nullable=enable

// Prints the time zone ids of the tz database for catalog.timeZones of ../project-settings.json: every canonical zone and
// every link (Asia/Calcutta next to Asia/Kolkata, so the spelling of any browser is in it), the Etc/* zones and UTC,
// sorted by ordinal comparison, one JSON string per line. The tz database is the one NodaTime ships (its version goes to
// the standard error). Copy the lines into the catalog after a NodaTime update that brings a new tz database release:
//
//   dotnet run src/Platform/Storyteller/conformance/tools/time-zones.cs
//
// See ../README.md.
using NodaTime;
using NodaTime.TimeZones;

var ids = new SortedSet<string>(DateTimeZoneProviders.Tzdb.Ids, StringComparer.Ordinal) { "UTC" };

Console.Error.WriteLine($"tz database {TzdbDateTimeZoneSource.Default.TzdbVersion}, {ids.Count} ids");

var lines = ids.Select(id => $"      \"{id}\"").ToList();

for (var index = 0; index < lines.Count; index++)
{
    Console.WriteLine(index < lines.Count - 1 ? $"{lines[index]}," : lines[index]);
}
