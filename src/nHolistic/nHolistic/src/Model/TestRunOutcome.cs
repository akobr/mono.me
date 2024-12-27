namespace _42.nHolistic;

public enum TestRunOutcome
{
    /// <summary>
    /// Test Case Does Not Have an outcome.
    /// </summary>
    None = 0,

    /// <summary>
    /// Test Case Passed
    /// </summary>
    Passed = 1,

    /// <summary>
    /// Test Case Failed
    /// </summary>
    Failed = 2,

    /// <summary>
    /// Test Case Skipped
    /// </summary>
    Skipped = 3,

    /// <summary>
    /// Test Case Not found
    /// </summary>
    NotFound = 4,
}
