using System;

namespace _42.Platform.Cli.Services;

public interface IBrowserLauncher
{
    // False when the browser could not be started; the caller tells the user to open the address by hand.
    bool TryOpen(Uri address);
}
