namespace _42.Platform.Cli.Commands.Configuration;

// What --mask hides in the log of a GitHub Actions job.
internal enum MaskMode
{
    // Values that resolution produced.
    Bound = 1,

    // Every string and number.
    All,
}
