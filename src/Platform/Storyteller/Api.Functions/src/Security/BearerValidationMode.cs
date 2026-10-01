namespace _42.Platform.Storyteller.Api.Security;

internal static class BearerValidationMode
{
    // Debug builds decode bearer tokens without checking the signature, which is the previous DEV_AUTH behaviour.
    // Tests set this so the validating path can run against a Debug build of the Functions app.
    public static bool DecodeWithoutValidation { get; set; } =
#if DEV_AUTH
        true;
#else
        false;
#endif
}
