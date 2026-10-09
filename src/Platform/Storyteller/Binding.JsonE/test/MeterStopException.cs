using System;

namespace _42.Platform.Storyteller.Binding.JsonE.UnitTests;

internal sealed class MeterStopException : Exception
{
    public MeterStopException(string reason)
        : base($"Meter stopped the evaluation: {reason}.")
    {
        Reason = reason;
    }

    public string Reason { get; }
}
