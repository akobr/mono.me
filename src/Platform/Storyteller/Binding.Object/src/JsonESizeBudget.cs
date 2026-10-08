namespace _42.Platform.Storyteller.Binding.Object;

/// <summary>
/// Running total of serialized value sizes and wrapped evaluations for one JSON-e evaluation.
/// </summary>
internal sealed class JsonESizeBudget
{
    public int Used { get; private set; }

    public int Steps { get; private set; }

    public void AddStep()
    {
        if (++Steps > ConfigurationBindingResolver.MaxEvaluationSteps)
        {
            throw new BindingEvaluationException(
                $"JSON-e evaluation exceeds {ConfigurationBindingResolver.MaxEvaluationSteps} steps.");
        }
    }

    public void Add(int length)
    {
        EnsureFits(length);
        Used += length;
    }

    public void EnsureFits(int additional)
    {
        if ((long)Used + additional > ConfigurationBindingResolver.MaxConcatLength)
        {
            throw new BindingEvaluationException(
                $"JSON-e value size exceeds {ConfigurationBindingResolver.MaxConcatLength} characters.");
        }
    }
}
