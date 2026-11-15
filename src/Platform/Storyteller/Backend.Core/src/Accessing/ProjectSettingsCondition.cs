namespace _42.Platform.Storyteller.Accessing;

// The condition of a settings write, from If-Match: null Versions and not Any for no condition; Any for *, which every
// project's settings meet (they always exist, at version 0 before the first write); otherwise the versions of a strong
// If-Match list, of which the stored version must be one.
public sealed record class ProjectSettingsCondition(IReadOnlyList<long>? Versions, bool Any)
{
    public static ProjectSettingsCondition None { get; } = new(null, false);

    public static ProjectSettingsCondition Anything { get; } = new(null, true);

    public static ProjectSettingsCondition Version(long version) => new([version], false);

    public static ProjectSettingsCondition Of(IEnumerable<long> versions) => new(versions.Distinct().ToList(), false);

    public bool Holds(long version)
    {
        return Any || Versions is null || Versions.Contains(version);
    }
}
