using System;

namespace _42.Platform.Cli.Configuration;

// The settings file cannot be read or written. The message names the file and the reason.
public sealed class UserSettingsException : Exception
{
    public UserSettingsException(string message, bool isDamaged, Exception? innerException = null)
        : base(message, innerException)
    {
        IsDamaged = isDamaged;
    }

    // The file is no valid settings file: its content is the problem, not the disk.
    public bool IsDamaged { get; }

    // A damaged file is wrong input, which the user fixes; a file that cannot be read or written is a failure.
    public int ExitCode => IsDamaged ? ExitCodes.ERROR_WRONG_INPUT : ExitCodes.ERROR_CRASH;
}
