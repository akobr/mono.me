using System.Runtime.CompilerServices;
using Json.JsonE;

namespace _42.Platform.Storyteller.Binding.Object;

/// <summary>
/// Meters the work of one resolved read across all of its engine calls.
/// Steps, allocated bytes and time accumulate per read. Recursion depth is tracked per engine call.
/// The first exceeded limit is latched, so every later report throws again.
/// </summary>
internal sealed class EvaluationMeter : IEvaluationMeter
{
    // Reading the clock costs more than a tick. Time grows linearly between ticks, so sampling it every 16 steps
    // only adds a bounded overshoot. Allocation is checked on every tick: values can double per step.
    private const int TimeCheckInterval = 16;

    [ThreadStatic]
    private static EvaluationMeter? _current;

    private readonly TimeProvider _timeProvider;
    private readonly long _maxTimestampTicks;

    private long _steps;
    private long _closedAllocatedBytes;
    private long _closedTimestampTicks;
    private long _scopeAllocatedStart;
    private long _scopeTimestampStart;
    private int _depth;
    private int _timeCheckCountdown;
    private bool _active;
    private EvaluationLimitKind? _exceeded;

    public EvaluationMeter(ObjectBindingLimits limits, TimeProvider timeProvider)
    {
        Limits = limits;
        _timeProvider = timeProvider;
        var maxTimestampTicks = limits.MaxEvaluationTime.TotalSeconds * timeProvider.TimestampFrequency;
        _maxTimestampTicks = maxTimestampTicks >= long.MaxValue ? long.MaxValue : (long)maxTimestampTicks;
    }

    /// <summary>
    /// Gets the meter of the engine call running on this thread, or <c>null</c> outside an engine call.
    /// </summary>
    public static EvaluationMeter? Current => _current;

    public ObjectBindingLimits Limits { get; }

    public long Steps => _steps;

    public long AllocatedBytes => _closedAllocatedBytes + (_active ? ScopeAllocatedBytes() : 0);

    public TimeSpan Elapsed => ToTimeSpan(_closedTimestampTicks + (_active ? ScopeTimestampTicks() : 0));

    public EvaluationLimitKind? Exceeded => _exceeded;

    /// <summary>
    /// Starts metering one synchronous engine call on the current thread.
    /// </summary>
    public Scope Enter()
    {
        if (_active)
        {
            throw new InvalidOperationException("An object binding engine call is already being metered.");
        }

        var previous = _current;
        _current = this;
        _active = true;
        _depth = 0;
        _scopeAllocatedStart = GC.GetAllocatedBytesForCurrentThread();
        _scopeTimestampStart = _timeProvider.GetTimestamp();
        return new Scope(this, previous);
    }

    public void Tick(int weight = 1)
    {
        ThrowIfExceeded();

        _steps += weight;
        if (_steps > Limits.MaxSteps)
        {
            Fail(EvaluationLimitKind.Steps);
        }

        if (_active)
        {
            if (_closedAllocatedBytes + ScopeAllocatedBytes() > Limits.MaxAllocatedBytes)
            {
                Fail(EvaluationLimitKind.Memory);
            }

            if (--_timeCheckCountdown <= 0)
            {
                _timeCheckCountdown = TimeCheckInterval;
                if (_closedTimestampTicks + ScopeTimestampTicks() > _maxTimestampTicks)
                {
                    Fail(EvaluationLimitKind.Time);
                }
            }
        }
    }

    public void EnterFrame()
    {
        ThrowIfExceeded();

        if (_depth >= Limits.MaxDepth || !RuntimeHelpers.TryEnsureSufficientExecutionStack())
        {
            Fail(EvaluationLimitKind.Depth);
        }

        _depth++;
    }

    public void ExitFrame()
    {
        if (_depth > 0)
        {
            _depth--;
        }
    }

    /// <summary>
    /// Fails with <see cref="EvaluationLimitKind.Memory"/> before a primitive allocates a value of <paramref name="bytes"/>
    /// that would pass the allocation budget. The value is not charged here; the real allocation is measured afterwards.
    /// </summary>
    public void Reserve(long bytes)
    {
        ThrowIfExceeded();

        if (_active && _closedAllocatedBytes + ScopeAllocatedBytes() + bytes > Limits.MaxAllocatedBytes)
        {
            Fail(EvaluationLimitKind.Memory);
        }
    }

    /// <summary>
    /// Checks allocation and time without sampling. Called when an engine returns, so the work of its last primitive,
    /// which no later step observes, still counts.
    /// </summary>
    public void CheckResources()
    {
        ThrowIfExceeded();

        if (!_active)
        {
            return;
        }

        if (_closedAllocatedBytes + ScopeAllocatedBytes() > Limits.MaxAllocatedBytes)
        {
            Fail(EvaluationLimitKind.Memory);
        }

        if (_closedTimestampTicks + ScopeTimestampTicks() > _maxTimestampTicks)
        {
            Fail(EvaluationLimitKind.Time);
        }
    }

    public void ThrowIfExceeded()
    {
        if (_exceeded is { } kind)
        {
            throw CreateException(kind);
        }
    }

    private void Exit(EvaluationMeter? previous)
    {
        _closedAllocatedBytes += ScopeAllocatedBytes();
        _closedTimestampTicks += ScopeTimestampTicks();
        _depth = 0;
        _active = false;
        _current = previous;
    }

    private long ScopeAllocatedBytes()
    {
        return GC.GetAllocatedBytesForCurrentThread() - _scopeAllocatedStart;
    }

    private long ScopeTimestampTicks()
    {
        return _timeProvider.GetTimestamp() - _scopeTimestampStart;
    }

    private TimeSpan ToTimeSpan(long timestampTicks)
    {
        return TimeSpan.FromSeconds((double)timestampTicks / _timeProvider.TimestampFrequency);
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private void Fail(EvaluationLimitKind kind)
    {
        _exceeded = kind;
        throw CreateException(kind);
    }

    private EvaluationLimitExceededException CreateException(EvaluationLimitKind kind)
    {
        return kind switch
        {
            EvaluationLimitKind.Steps => new EvaluationLimitExceededException(
                kind,
                Limits.MaxSteps,
                $"Object binding evaluation exceeds {Limits.MaxSteps} steps in one read."),
            EvaluationLimitKind.Memory => new EvaluationLimitExceededException(
                kind,
                Limits.MaxAllocatedBytes,
                $"Object binding evaluation exceeds {Limits.MaxAllocatedBytes} allocated bytes in one read."),
            EvaluationLimitKind.Time => new EvaluationLimitExceededException(
                kind,
                (long)Limits.MaxEvaluationTime.TotalMilliseconds,
                $"Object binding evaluation exceeds {(long)Limits.MaxEvaluationTime.TotalMilliseconds} ms in one read."),
            _ => new EvaluationLimitExceededException(
                EvaluationLimitKind.Depth,
                Limits.MaxDepth,
                $"Object binding evaluation exceeds nesting depth {Limits.MaxDepth} or the available stack."),
        };
    }

    public readonly struct Scope : IDisposable
    {
        private readonly EvaluationMeter _meter;
        private readonly EvaluationMeter? _previous;

        public Scope(EvaluationMeter meter, EvaluationMeter? previous)
        {
            _meter = meter;
            _previous = previous;
        }

        public void Dispose()
        {
            _meter.Exit(_previous);
        }
    }
}
