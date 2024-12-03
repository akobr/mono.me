using MediatR;

namespace _42.nHolistic;

public class TestCaseDiscoveredNotification : INotification
{
    public required TestCase TestCase { get; init; }
}
