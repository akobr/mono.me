using System;

namespace _42.Platform.Storyteller.DbCreator.DataSets;

// Wrong input of a seed: an unknown set, an invalid manifest or annotations.jsonl, a cycle of dependencies, a bad name,
// an organization the owner does not own. Thrown before anything of the set is written.
public sealed class DataSetException : Exception
{
    public DataSetException(string message, Exception? innerException = null)
        : base(message, innerException)
    {
    }
}
