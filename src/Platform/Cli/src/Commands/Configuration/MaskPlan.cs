using System.Collections.Generic;
using _42.Platform.Storyteller.ConfigurationFormats;

namespace _42.Platform.Cli.Commands.Configuration;

// Masked: the values that get a mask; TooShort: bound values left unmasked under Bound; Data: what each ::add-mask::
// command carries, in document order.
internal sealed record MaskPlan(IReadOnlyList<DocumentLeaf> Masked, IReadOnlyList<DocumentLeaf> TooShort, IReadOnlyList<string> Data)
{
    // Never the values.
    public override string ToString()
    {
        return $"{Masked.Count} masked, {TooShort.Count} too short, {Data.Count} masks";
    }
}
