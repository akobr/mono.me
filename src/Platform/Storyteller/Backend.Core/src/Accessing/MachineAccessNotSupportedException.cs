namespace _42.Platform.Storyteller.Accessing;

// A machine credential kind this deployment cannot issue. The API answers it with 400.
public sealed class MachineAccessNotSupportedException : InvalidOperationException
{
    public MachineAccessNotSupportedException(string message)
        : base(message)
    {
    }
}
