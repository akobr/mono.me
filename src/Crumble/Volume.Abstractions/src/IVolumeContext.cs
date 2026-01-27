namespace _42.Crumble;

public interface IVolumeContext
{
    string? VolumeKey { get; }

    string? RootPath { get; }

    string? SubPath { get; }
}
