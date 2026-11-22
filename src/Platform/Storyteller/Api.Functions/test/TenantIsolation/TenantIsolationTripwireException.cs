namespace _42.Platform.Storyteller.Api.Functions.UnitTests.TenantIsolation;

// Thrown by every tripwire: the request got past the gate. ExceptionHandlingMiddleware answers it with 500.
internal sealed class TenantIsolationTripwireException(TripwireCall call)
    : Exception($"The request reached {call.Service}.{call.Member}.");
