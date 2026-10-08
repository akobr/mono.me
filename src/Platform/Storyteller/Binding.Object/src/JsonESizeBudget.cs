namespace _42.Platform.Storyteller.Binding.Object;

/// <summary>
/// Running total of serialized value sizes for one JSON-e evaluation.
/// Step counts go to the read-wide <see cref="ConfigurationBindingResolver.EvaluationBudget"/>.
/// </summary>
internal sealed class JsonESizeBudget
{
    private readonly ConfigurationBindingResolver.EvaluationBudget _read;

    public JsonESizeBudget(ConfigurationBindingResolver.EvaluationBudget read)
    {
        _read = read;
    }

    public int Used { get; private set; }

    public void AddStep()
    {
        _read.AddStep();
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
