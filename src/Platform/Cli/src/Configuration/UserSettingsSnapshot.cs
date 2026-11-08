using System;
using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json.Linq;

namespace _42.Platform.Cli.Configuration;

// The effective sections: the section of the settings file when the file has it, even an empty one, else the section of
// its legacy file. The migration copies whole sections, so the settings file replaces a legacy section as a whole.
public sealed class UserSettingsSnapshot
{
    private readonly IReadOnlyDictionary<string, UserSettingsSection> _sections;

    private UserSettingsSnapshot(IReadOnlyDictionary<string, UserSettingsSection> sections)
    {
        _sections = sections;
    }

    public IEnumerable<UserSettingsSection> Sections => UserSettingsSchema.SectionNames.Select(name => _sections[name]);

    public UserSettingsSection this[string section] => _sections[section];

    public static UserSettingsSnapshot From(JObject settings, Func<string, JObject?> loadLegacy)
    {
        var sections = new Dictionary<string, UserSettingsSection>(StringComparer.OrdinalIgnoreCase);

        foreach (var name in UserSettingsSchema.SectionNames)
        {
            if (UserSettingsSchema.GetValue(settings, name) is JObject raw)
            {
                sections[name] = new UserSettingsSection(name, UserSettingsSchema.Normalize(name, raw), UserSettingsLayer.Settings);
            }
            else if (loadLegacy(name) is { } legacy)
            {
                sections[name] = new UserSettingsSection(name, legacy, UserSettingsLayer.Legacy);
            }
            else
            {
                sections[name] = new UserSettingsSection(name, new JObject(), UserSettingsLayer.None);
            }
        }

        return new UserSettingsSnapshot(sections);
    }
}
