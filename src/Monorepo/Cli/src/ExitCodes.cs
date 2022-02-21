namespace _42.Monorepo.Cli
{
    public static class ExitCodes
    {
        public const int SUCCESS = 0;

        // input errors negative
        public const int ERROR_INPUT_PARSING = -1;

        // errors 1 - 39
        public const int ERROR_CRASH = 1;
        public const int ERROR_WRONG_INPUT = 2;
        public const int ERROR_WRONG_PLACE = 3;

        // warnigs from 40
        public const int WARNING_ABORTED = 40;
        public const int WARNING_NO_WORK_NEEDED = 42;
    }
}
