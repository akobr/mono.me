namespace _42.Platform.Storyteller.Accessing;

// A machine credential kind or an operation this deployment or project cannot serve, or the machine's kind does not
// have. The API answers it with 400 and MachineAccessNotSupported.
public sealed class MachineAccessNotSupportedException(string message)
    : StorytellerException(message, ErrorCodes.MachineAccessNotSupported);
