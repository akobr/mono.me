using System;
using System.Runtime.CompilerServices;

// Storyteller patch: evaluation metering hook. Not part of upstream JsonE.Net. See VENDORED.md.

namespace Json.JsonE;

/// <summary>
/// Receives work reports from the interpreter while a template is evaluated.
/// An implementation throws to stop the evaluation.
/// </summary>
public interface IEvaluationMeter
{
	/// <summary>
	/// Reports one unit of interpreter work (a template node, an expression node, a parse, an interpolation hole, a produced item).
	/// </summary>
	void Tick(int weight);

	/// <summary>
	/// Reports that the interpreter enters one level of recursion.
	/// </summary>
	void EnterFrame();

	/// <summary>
	/// Reports that the interpreter leaves one level of recursion.
	/// </summary>
	void ExitFrame();
}

internal static class Metering
{
	[ThreadStatic]
	private static IEvaluationMeter? _current;

	internal static IEvaluationMeter? Current
	{
		get => _current;
		set => _current = value;
	}

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	internal static void Tick(int weight = 1)
	{
		_current?.Tick(weight);
	}

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	internal static FrameScope Frame()
	{
		var meter = _current;
		meter?.EnterFrame();
		return new FrameScope(meter);
	}

	internal readonly struct FrameScope : IDisposable
	{
		private readonly IEvaluationMeter? _meter;

		public FrameScope(IEvaluationMeter? meter)
		{
			_meter = meter;
		}

		public void Dispose()
		{
			_meter?.ExitFrame();
		}
	}
}
