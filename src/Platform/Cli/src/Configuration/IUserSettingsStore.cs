using System.Collections.Generic;
using Newtonsoft.Json.Linq;

namespace _42.Platform.Cli.Configuration;

// sform.settings.json in the .42for.net folder of the user, and the legacy files next to the assembly that it replaces.
public interface IUserSettingsStore
{
    string FilePath { get; }

    // The settings file as it is written, or an empty object when it does not exist or is blank. A damaged file (no JSON
    // object, or a section of it that is no object) or one that cannot be read throws UserSettingsException.
    JObject Load();

    // The normalised section of its legacy file; null when the file is missing, damaged or has nothing of the section.
    JObject? LoadLegacy(string section);

    // The effective sections (see UserSettingsSnapshot); throws UserSettingsException like Load.
    UserSettingsSnapshot Read();

    // Sets (a value) or removes (null) properties of a section and writes the file; every other property and section
    // stays. The sections that only a legacy file has are copied in first, and the legacy files are deleted where possible.
    // A damaged file is never overwritten: it throws UserSettingsException, as does a failed write.
    void Update(string section, IReadOnlyDictionary<string, JToken?> changes);
}
