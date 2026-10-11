using System;

namespace _42.Platform.Storyteller.Annotating;

// A concurrent change made an ETag check or a create fail; the operation is computed again from fresh state.
internal sealed class AnnotationConcurrencyException(string message)
    : Exception(message);
