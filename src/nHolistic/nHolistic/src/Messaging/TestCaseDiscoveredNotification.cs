using MediatR;

namespace _42.tHolistic;

public class TestCaseDiscoveredNotification : INotification
{
    public required TestCase TestCase { get; init; }
}
